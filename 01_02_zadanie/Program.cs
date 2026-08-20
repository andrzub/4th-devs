using _01_02_zadanie.LLM;
using _01_02_zadanie.Tools;
using Microsoft.Extensions.Configuration;

// ---------------------------------------------------------------------------
// S01E02 "findhim" — agent loop with Function Calling.
// The model iterates over the suspects from S01E01, queries the hub API via
// tools, finds who was closest to a power plant and submits the report itself.
// ---------------------------------------------------------------------------

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var aiDevsApiKey       = config["AI_DevsApiKey"]       ?? throw new InvalidOperationException("AI_DevsApiKey is not configured.");
var openAiApiKey       = config["OpenAI:ApiKey"]       ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
var openAiBaseUrl      = config["OpenAI:BaseUrl"]      ?? "https://api.openai.com/v1";
var openAiDefaultModel = config["OpenAI:DefaultModel"] ?? "gpt-5-mini";

var suspectsJson    = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "transport_people_response.json"));
var powerPlantsJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "findhim_locations.json"));

ILlmClient llm = new OpenAiLlmClient(openAiApiKey, openAiBaseUrl, openAiDefaultModel);

var tools = new List<ITool>
{
    new GetPersonLocationsTool(aiDevsApiKey),
    new GetPersonAccessLevelTool(aiDevsApiKey),
    new FindNearestPowerPlantTool(),
    new SubmitFindhimReportTool(aiDevsApiKey)
};
var toolsByName = tools.ToDictionary(t => t.Name);

var systemPrompt = $"""
    You are an investigator agent. Your mission: find which suspect was seen closest to one
    of the nuclear power plants, determine their access level and submit the final report.

    <suspects>
    {suspectsJson}
    </suspects>

    <power_plants>
    {powerPlantsJson}
    </power_plants>

    The power plant list contains only Polish city names and PWR codes, no coordinates.
    Use your own knowledge of the geographic coordinates of these Polish cities (city centre).

    Follow this procedure strictly, phase by phase:

    Phase 1 — call get_person_locations for ALL suspects (in parallel) before any analysis.
    Phase 2 — write down your best-known latitude/longitude of every power plant city centre.
      Be careful and precise; use the same coordinates consistently for every suspect.
    Phase 3 — for EVERY suspect call find_nearest_power_plant once, passing all their sightings
      and the full plant list with your coordinates. Do not skip any suspect or sighting.
    Phase 4 — the wanted person is the suspect with the SMALLEST sighting-to-plant distance
      overall (expected to be well under 5 km). Only for that suspect call
      get_person_access_level (birthYear = the "born" field, integer).
    Phase 5 — call submit_findhim_report once with name, surname, accessLevel and the PWR code
      of that closest plant.

    If the hub rejects the report, do NOT resubmit the same data. Move to the suspect with the
    next-smallest distance (fetch their access level first) and submit that one instead.
    You may request multiple tool calls in parallel in a single turn.
    Finish with a short summary that includes the hub response and the flag if present.
    """;

var messages = new List<Message>
{
    Message.System(systemPrompt),
    Message.User("Find the suspect, determine their access level and submit the findhim report.")
};

const int MaxIterations = 20;
var totalUsage = 0;

for (var iteration = 1; iteration <= MaxIterations; iteration++)
{
    Console.WriteLine($"--- Iteration {iteration} ---");

    var response = await llm.CompleteAsync(new LlmRequest { Messages = messages, Tools = tools });
    totalUsage += response.Usage.TotalTokens;

    if (!response.HasToolCalls)
    {
        Console.WriteLine();
        Console.WriteLine(response.Content ?? "(no content)");
        break;
    }

    messages.Add(Message.AssistantToolCalls(response.ContentRaw, response.ToolCalls));

    foreach (var toolCall in response.ToolCalls)
    {
        var argsPreview = toolCall.ArgumentsJson.Length > 120 ? toolCall.ArgumentsJson[..120] + "…" : toolCall.ArgumentsJson;
        Console.WriteLine($"  -> {toolCall.FunctionName} {argsPreview}");

        var result = toolsByName.TryGetValue(toolCall.FunctionName, out var tool)
            ? await tool.ExecuteAsync(toolCall.ArgumentsJson)
            : $"Unknown tool '{toolCall.FunctionName}'. Available tools: {string.Join(", ", toolsByName.Keys)}.";

        var resultPreview = result.Length > 200 ? result[..200] + "…" : result;
        Console.WriteLine($"  <- {resultPreview}");

        messages.Add(Message.ToolResult(toolCall.Id, result));
    }

    if (iteration == MaxIterations)
    {
        Console.WriteLine("Reached the iteration limit without a final answer — aborting.");
    }
}

Console.WriteLine();
Console.WriteLine($"Total tokens used: {totalUsage}");
