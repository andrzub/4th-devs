using System.Globalization;
using System.Text.Json.Nodes;
using _01_05_zadanie.Llm;
using _01_05_zadanie.Railway;
using _01_05_zadanie.Tools;
using Microsoft.Extensions.Configuration;

// ---------------------------------------------------------------------------
// S01E05 "railway" — agent activating a railway route through an undocumented API.
// The API documents itself through its "help" action, so the agent discovers the
// action names and their order at runtime instead of being told them.
//
// Every action goes to the hub's /verify endpoint, so the whole run is opt-in:
// without --run nothing is sent and the program only prints what it would do.
// ---------------------------------------------------------------------------

var runEnabled = args.Any(a => a.Equals("--run", StringComparison.OrdinalIgnoreCase)
                            || a.Equals("--submit", StringComparison.OrdinalIgnoreCase));

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var aiDevsApiKey       = config["AI_DevsApiKey"]       ?? throw new InvalidOperationException("AI_DevsApiKey is not configured.");
var openAiApiKey       = config["OpenAI:ApiKey"]       ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
var openAiBaseUrl      = config["OpenAI:BaseUrl"]      ?? "https://api.openai.com/v1";
var openAiDefaultModel = config["OpenAI:DefaultModel"] ?? "gpt-4.1";

var verifyUrl     = config["Railway:VerifyUrl"] ?? "https://hub.ag3nts.org/verify";
var taskName      = config["Railway:TaskName"]  ?? "railway";
var routeName     = config["Railway:RouteName"] ?? "X-01";
var minInterval   = ParseDouble(config["Railway:MinIntervalSeconds"], 1.0);
var maxWait       = ParseDouble(config["Railway:MaxWaitSeconds"], 900);
var maxAttempts   = ParseInt(config["Railway:MaxAttemptsPerCall"], 8);
var maxIterations = ParseInt(config["Railway:MaxIterations"], 30);

var logPath = Path.Combine(AppContext.BaseDirectory, "railway-log.jsonl");

Console.WriteLine($"Zadanie: {taskName} | trasa do aktywacji: {routeName}");
Console.WriteLine($"Model: {openAiDefaultModel} | endpoint: {verifyUrl}");

if (!runEnabled)
{
    var samplePayload = new JsonObject
    {
        ["apikey"] = "***",
        ["task"]   = taskName,
        ["answer"] = new JsonObject { ["action"] = "help" }
    };

    Console.WriteLine();
    Console.WriteLine("Tryb: dry-run — NIC nie zostało wysłane. Agent nie wystartował.");
    Console.WriteLine("Pierwsze żądanie, które agent wykonałby po uruchomieniu z --run:");
    Console.WriteLine($"  POST {verifyUrl}");
    Console.WriteLine($"  {samplePayload.ToJsonString()}");
    Console.WriteLine();
    Console.WriteLine("Aby faktycznie uruchomić agenta: dotnet run -- --run");
    return;
}

ILlmClient llm = new OpenAiLlmClient(openAiApiKey, openAiBaseUrl, openAiDefaultModel);

var railwayClient = new RailwayClient(aiDevsApiKey, new RailwayClientOptions
{
    VerifyUrl          = verifyUrl,
    TaskName           = taskName,
    MinInterval        = TimeSpan.FromSeconds(minInterval),
    MaxWait            = TimeSpan.FromSeconds(maxWait),
    MaxAttemptsPerCall = maxAttempts,
    LogPath            = logPath
});

var railwayTool = new RailwayApiTool(railwayClient);
var tools = new List<ITool> { railwayTool };
var toolsByName = tools.ToDictionary(t => t.Name);

var messages = new List<Message>
{
    Message.System(RailwayAgentPrompt.Build(routeName)),
    Message.User($"Activate route {routeName}. Begin by asking the API for its documentation.")
};

Console.WriteLine($"Tryb: --run — agent BĘDZIE wywoływał API Huba. Log: {logPath}");
Console.WriteLine();

var totalTokens = 0;

for (var iteration = 1; iteration <= maxIterations; iteration++)
{
    Console.WriteLine($"--- Iteration {iteration} ---");

    var response = await llm.CompleteAsync(new LlmRequest { Messages = messages, Tools = tools });
    totalTokens += response.Usage.TotalTokens;

    if (!response.HasToolCalls)
    {
        Console.WriteLine();
        Console.WriteLine(response.Content ?? "(no content)");
        break;
    }

    if (response.ContentRaw is { Length: > 0 } reasoning)
        Console.WriteLine($"  {Preview(reasoning, 300)}");

    messages.Add(Message.AssistantToolCalls(response.ContentRaw, response.ToolCalls));

    foreach (var toolCall in response.ToolCalls)
    {
        Console.WriteLine($"  -> {toolCall.FunctionName} {Preview(toolCall.ArgumentsJson, 200)}");

        var result = toolsByName.TryGetValue(toolCall.FunctionName, out var tool)
            ? await tool.ExecuteAsync(toolCall.ArgumentsJson)
            : $"Unknown tool '{toolCall.FunctionName}'. Available tools: {string.Join(", ", toolsByName.Keys)}.";

        Console.WriteLine($"  <- {Preview(result, 400)}");

        messages.Add(Message.ToolResult(toolCall.Id, result));
    }

    if (railwayTool.FlagFound)
    {
        // One more turn so the model can report the action sequence that worked.
        var closing = await llm.CompleteAsync(new LlmRequest { Messages = messages, Tools = tools });
        totalTokens += closing.Usage.TotalTokens;

        Console.WriteLine();
        Console.WriteLine(closing.Content ?? "(trasa aktywowana — agent nie dodał podsumowania)");
        break;
    }

    if (iteration == maxIterations)
        Console.WriteLine("Reached the iteration limit without activating the route — aborting.");
}

Console.WriteLine();
Console.WriteLine($"Tokeny LLM: {totalTokens} | żądania do API: {railwayClient.RequestsSent} | czas oczekiwania: {railwayClient.TotalWaited.TotalSeconds:0}s");
Console.WriteLine($"Log wywołań: {logPath}");

if (railwayTool.Flag is { } flag)
{
    Console.WriteLine();
    Console.WriteLine($"FLAGA: {flag}");
    Console.WriteLine("Wpisz ją na https://hub.ag3nts.org/ — nie commituj jej do repo.");
}

static double ParseDouble(string? raw, double fallback) =>
    double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

static int ParseInt(string? raw, int fallback) =>
    int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

static string Preview(string text, int limit)
{
    var flattened = text.ReplaceLineEndings(" ");
    return flattened.Length > limit ? flattened[..limit] + "…" : flattened;
}
