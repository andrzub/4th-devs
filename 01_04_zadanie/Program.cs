using _01_04_zadanie.Documents;
using _01_04_zadanie.Llm;
using _01_04_zadanie.Mission;
using _01_04_zadanie.Tools;
using Microsoft.Extensions.Configuration;

// ---------------------------------------------------------------------------
// S01E04 "sendit" — agent filling the SPK transport declaration.
// The documentation is scattered across a dozen files and part of it is a PNG,
// so the agent reaches for documents and for vision through tools rather than
// getting everything up front.
//
// Posting to the hub is opt-in: run with --submit to actually send the answer.
// ---------------------------------------------------------------------------

var submitToHub = args.Contains("--submit", StringComparer.OrdinalIgnoreCase);

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
var openAiVisionModel  = config["OpenAI:VisionModel"]  ?? openAiDefaultModel;
var docsBaseUrl        = config["Spk:DocsBaseUrl"]     ?? "https://hub.ag3nts.org/dane/doc/";

ILlmClient llm = new OpenAiLlmClient(openAiApiKey, openAiBaseUrl, openAiDefaultModel);

var library = new DocumentLibrary(docsBaseUrl, Path.Combine(AppContext.BaseDirectory, "docs-cache"));
var declarationPath = Path.Combine(AppContext.BaseDirectory, "deklaracja.txt");

var submitTool = new SubmitDeclarationTool(declarationPath, aiDevsApiKey, submitToHub);

var tools = new List<ITool>
{
    new FetchDocumentTool(library),
    new AnalyzeImageTool(library, llm, openAiVisionModel),
    submitTool
};
var toolsByName = tools.ToDictionary(t => t.Name);

var today = DateOnly.FromDateTime(DateTime.Now);

var messages = new List<Message>
{
    Message.System(DeclarationAgentPrompt.Build(today)),
    Message.User("Prepare the SPK declaration for this shipment and finalise it.")
};

Console.WriteLine($"Model: {openAiDefaultModel} | vision: {openAiVisionModel}");
Console.WriteLine(submitToHub
    ? "Tryb: --submit — gotowa deklaracja ZOSTANIE wysłana do Huba."
    : "Tryb: dry-run — deklaracja zostanie tylko zapisana. Użyj --submit, aby wysłać do Huba.");
Console.WriteLine();

const int MaxIterations = 25;
var totalTokens = 0;

for (var iteration = 1; iteration <= MaxIterations; iteration++)
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

    messages.Add(Message.AssistantToolCalls(response.ContentRaw, response.ToolCalls));

    foreach (var toolCall in response.ToolCalls)
    {
        Console.WriteLine($"  -> {toolCall.FunctionName} {Preview(toolCall.ArgumentsJson, 160)}");

        var result = toolsByName.TryGetValue(toolCall.FunctionName, out var tool)
            ? await tool.ExecuteAsync(toolCall.ArgumentsJson)
            : $"Unknown tool '{toolCall.FunctionName}'. Available tools: {string.Join(", ", toolsByName.Keys)}.";

        Console.WriteLine($"  <- {Preview(result, 200)}");

        messages.Add(Message.ToolResult(toolCall.Id, result));
    }

    if (submitTool.Accepted)
    {
        // One more turn so the model can close with its summary of how it derived the fields.
        var closing = await llm.CompleteAsync(new LlmRequest { Messages = messages, Tools = tools });
        totalTokens += closing.Usage.TotalTokens;

        Console.WriteLine();
        Console.WriteLine(closing.Content ?? "(deklaracja gotowa — agent nie dodał podsumowania)");
        break;
    }

    if (iteration == MaxIterations)
        Console.WriteLine("Reached the iteration limit without a finished declaration — aborting.");
}

Console.WriteLine();
Console.WriteLine($"Total tokens used: {totalTokens}");

static string Preview(string text, int limit)
{
    var flattened = text.ReplaceLineEndings(" ");
    return flattened.Length > limit ? flattened[..limit] + "…" : flattened;
}
