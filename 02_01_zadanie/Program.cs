using System.Globalization;
using System.Text.Json.Nodes;
using _02_01_zadanie.Categorize;
using _02_01_zadanie.Llm;
using _02_01_zadanie.Tools;
using Microsoft.Extensions.Configuration;

// ---------------------------------------------------------------------------
// S02E01 "categorize" — an agent acting as a prompt engineer for a remote,
// 100-token cargo classifier. The agent designs the prompt template; the code
// deterministically measures it (o200k_base), enforces the token margin, runs
// the submission cycles and absorbs transport errors. Every classification
// query goes to the hub's /verify endpoint, so the whole run is opt-in:
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

var logPath = Path.Combine(AppContext.BaseDirectory, "categorize-log.jsonl");

var options = new CategorizeOptions
{
    VerifyUrl          = config["Categorize:VerifyUrl"] ?? "https://hub.ag3nts.org/verify",
    CsvUrl             = (config["Categorize:CsvUrlTemplate"] ?? "https://hub.ag3nts.org/data/{apikey}/categorize.csv").Replace("{apikey}", aiDevsApiKey),
    TaskName           = config["Categorize:TaskName"] ?? "categorize",
    TokenLimit         = ParseInt(config["Categorize:TokenLimit"], 100),
    TokenSafetyMargin  = ParseInt(config["Categorize:TokenSafetyMargin"], 8),
    BudgetPp           = ParseDouble(config["Categorize:BudgetPP"], 1.5),
    MinInterval        = TimeSpan.FromSeconds(ParseDouble(config["Categorize:MinIntervalSeconds"], 0.5)),
    MaxAttemptsPerCall = ParseInt(config["Categorize:MaxAttemptsPerCall"], 6),
    LogPath            = logPath
};

var maxIterations = ParseInt(config["Categorize:MaxAgentIterations"], 24);

Console.WriteLine($"Zadanie: {options.TaskName} | okno klasyfikatora: {options.TokenLimit} tok. (margines {options.TokenSafetyMargin}) | budżet: {options.BudgetPp} PP");
Console.WriteLine($"Model: {openAiDefaultModel} | endpoint: {options.VerifyUrl}");

if (!runEnabled)
{
    var samplePayload = new JsonObject
    {
        ["apikey"] = "***",
        ["task"]   = options.TaskName,
        ["answer"] = new JsonObject { ["prompt"] = "reset" }
    };

    var sampleTemplate = "Answer with exactly one word, DNG or NEU. Item {id}: {description}";
    var sampleTokens = new TokenCounter().Count(sampleTemplate);

    Console.WriteLine();
    Console.WriteLine("Tryb: dry-run — NIC nie zostało wysłane. Agent nie wystartował.");
    Console.WriteLine($"Katalog byłby pobierany z: {RedactKey(options.CsvUrl, aiDevsApiKey)}");
    Console.WriteLine("Każde zapytanie klasyfikacyjne (i reset licznika) to POST na /verify w kształcie:");
    Console.WriteLine($"  POST {options.VerifyUrl}");
    Console.WriteLine($"  {samplePayload.ToJsonString()}");
    Console.WriteLine($"Tokenizer o200k_base działa lokalnie — przykładowy szablon \"{sampleTemplate}\" ma {sampleTokens} tok.");
    Console.WriteLine();
    Console.WriteLine("Aby faktycznie uruchomić agenta: dotnet run -- --run");
    return;
}

ILlmClient llm = new OpenAiLlmClient(openAiApiKey, openAiBaseUrl, openAiDefaultModel);

var hub = new HubClient(aiDevsApiKey, options);
var tokenCounter = new TokenCounter();

var validateTool = new ValidateTemplateTool(hub, tokenCounter, options);
var cycleTool = new RunClassificationCycleTool(hub, tokenCounter, options);
var tools = new List<ITool> { validateTool, cycleTool };
var toolsByName = tools.ToDictionary(t => t.Name);

var messages = new List<Message>
{
    Message.System(PromptEngineerPrompt.Build(options)),
    Message.User("Design the classification template and obtain the flag. Draft a template, validate it, then run classification cycles, improving the template after each report until the flag appears.")
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

        if (cycleTool.FlagFound)
            break;

        // The model is trying to finish without a real flag (gave up or fabricated one) — push back.
        messages.Add(Message.Assistant(response.Content ?? string.Empty));
        messages.Add(Message.User("No flag has been obtained — a flag is only real when it arrives in a tool result. The task is not finished: analyse the last cycle report and continue with an improved template."));
        Console.WriteLine("  [loop] model zakończył bez flagi — wymuszam kontynuację");
        continue;
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

    if (cycleTool.FlagFound)
    {
        // One more turn so the model can report the template that worked.
        var closing = await llm.CompleteAsync(new LlmRequest { Messages = messages, Tools = tools });
        totalTokens += closing.Usage.TotalTokens;

        Console.WriteLine();
        Console.WriteLine(closing.Content ?? "(flaga zdobyta — agent nie dodał podsumowania)");
        break;
    }

    if (iteration == maxIterations)
        Console.WriteLine("Reached the iteration limit without obtaining the flag — aborting.");
}

Console.WriteLine();
Console.WriteLine($"Tokeny LLM: {totalTokens} | żądania do /verify: {hub.VerifyRequestsSent} | pełne cykle: {cycleTool.CyclesRun}");
Console.WriteLine($"Log wywołań: {logPath}");

if (cycleTool.Flag is { } flag)
{
    Console.WriteLine();
    Console.WriteLine($"FLAGA: {flag}");
    Console.WriteLine("Wpisz ją na https://hub.ag3nts.org/ — nie commituj jej do repo.");
}
else
{
    Console.WriteLine();
    Console.WriteLine("Flaga NIE została zdobyta. Jeśli w tekście agenta pojawia się coś w stylu {FLG:...}, to fabrykacja — prawdziwa flaga przychodzi wyłącznie w odpowiedzi Huba.");
}

static double ParseDouble(string? raw, double fallback) =>
    double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

static int ParseInt(string? raw, int fallback) =>
    int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

static string RedactKey(string text, string key) =>
    key.Length > 0 ? text.Replace(key, "***") : text;

static string Preview(string text, int limit)
{
    var flattened = text.ReplaceLineEndings(" ");
    return flattened.Length > limit ? flattened[..limit] + "…" : flattened;
}
