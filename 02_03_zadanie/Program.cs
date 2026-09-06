using _02_03_zadanie.Analysis;
using _02_03_zadanie.Hub;
using _02_03_zadanie.Llm;
using _02_03_zadanie.Mission;
using _02_03_zadanie.Tools;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var analyzeMode = args.Contains("--analyze");
var checkIndex = Array.IndexOf(args, "--check");
var submitIndex = Array.IndexOf(args, "--submit");
var draftMode = args.Contains("--draft");
var runMode = args.Contains("--run");
var refresh = args.Contains("--refresh");

if (!analyzeMode && checkIndex < 0 && submitIndex < 0 && !draftMode && !runMode)
{
    Console.WriteLine("S02E03 'failure': plant log compression agent.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- --analyze        Print log statistics and collapsed event types. No LLM, no /verify.");
    Console.WriteLine("  dotnet run -- --check <file>   Validate a digest from a local file: format, match against the");
    Console.WriteLine("                                 source log, token count. No LLM, no /verify.");
    Console.WriteLine("  dotnet run -- --submit <file>  Validate a digest from a local file and, if it passes, send it");
    Console.WriteLine("                                 to /verify. No LLM. For finishing by hand.");
    Console.WriteLine("  dotnet run -- --draft          Agent loop with a simulated submit: the first digest that passes");
    Console.WriteLine("                                 the local checks is saved to log-cache/run-*/ and the run ends. No /verify.");
    Console.WriteLine("  dotnet run -- --run            Full agent loop. Every accepted submit_logs call is a /verify request.");
    Console.WriteLine();
    Console.WriteLine("  --refresh re-downloads the log even if a cached copy exists (log-cache/failure.log).");
    return;
}

var aiDevsApiKey = configuration["AI_DevsApiKey"];
if (string.IsNullOrWhiteSpace(aiDevsApiKey))
    throw new InvalidOperationException("Missing 'AI_DevsApiKey'. Set it in appsettings.Development.json.");

var hubBaseUrl = configuration["Failure:HubBaseUrl"] ?? "https://hub.ag3nts.org";
var cacheDirectory = configuration["Failure:CacheDirectory"] ?? "log-cache";
var tokenLimit = configuration.GetValue("Failure:TokenLimit", 1500);
var tokenSafetyMargin = configuration.GetValue("Failure:TokenSafetyMargin", 100);

var hub = new HubClient(hubBaseUrl, aiDevsApiKey, "failure-log.jsonl");
var logText = await hub.GetFailureLogAsync(Path.Combine(cacheDirectory, "failure.log"), refresh);
var log = LogParser.Parse(logText);
var tokens = new TokenCounter();
var checker = new DigestChecker(new DigestValidator(log), new TokenBudget(tokens, tokenLimit, tokenSafetyMargin));

if (analyzeMode)
{
    Console.WriteLine();
    Console.WriteLine(LogAnalyzer.RenderOverview(log, tokens));
    Console.WriteLine("EVENT TYPES, WARN AND ABOVE (identical messages collapsed, chronological by first occurrence):");
    Console.WriteLine(LogAnalyzer.RenderEventTypes(LogAnalyzer.GroupByMessage(log.Where(new LogFilter { MinLevel = "WARN" }))));
    return;
}

if (checkIndex >= 0)
{
    var digestPath = args.ElementAtOrDefault(checkIndex + 1)
        ?? throw new ArgumentException("Usage: dotnet run -- --check <path-to-digest.txt>");
    Console.WriteLine();
    Console.WriteLine(checker.Check(await File.ReadAllTextAsync(digestPath)).Render());
    return;
}

// Manual path: the same checks as the agent's submit tool, then one real /verify request.
if (submitIndex >= 0)
{
    var digestPath = args.ElementAtOrDefault(submitIndex + 1)
        ?? throw new ArgumentException("Usage: dotnet run -- --submit <path-to-digest.txt>");
    var result = checker.Check(await File.ReadAllTextAsync(digestPath));
    Console.WriteLine();
    Console.WriteLine(result.Render());
    if (!result.Passed)
    {
        Console.WriteLine("Not submitted: fix the problems above first.");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine("Submitting to /verify...");
    var response = await hub.SubmitLogsAsync(result.Digest, result.Budget.Tokens);
    Console.WriteLine(response);

    var manualState = new MissionState();
    manualState.ScanForFlag(response);
    Console.WriteLine(manualState.FlagReceived
        ? $"FLAG RECEIVED: {manualState.Flag}"
        : "No flag in the response. Read the feedback, adjust the file and submit again.");
    return;
}

// ---------------------------------------------------------------------------
// Agent loop (--draft / --run). In --run every accepted submit_logs call is a real /verify request.
// ---------------------------------------------------------------------------

var agentSettings = configuration.GetSection("Agent").Get<LlmProviderSettings>()
    ?? throw new InvalidOperationException("Missing 'Agent' configuration section.");
var scannerSettings = configuration.GetSection("Scanner").Get<LlmProviderSettings>()
    ?? throw new InvalidOperationException("Missing 'Scanner' configuration section.");

var agentClient = new OpenAiCompatibleLlmClient("Agent", agentSettings);
var scannerClient = new OpenAiCompatibleLlmClient("Scanner", scannerSettings);

var state = new MissionState();
var runDirectory = Path.Combine(cacheDirectory, $"run-{DateTime.Now:yyyyMMdd-HHmmss}");
var transcript = new Transcript(Path.Combine(runDirectory, "transcript.txt"));

var tools = new List<ITool>
{
    new LogOverviewTool(log, tokens),
    new ListEventTypesTool(log),
    new SearchLogTool(log),
    new SummarizeComponentTool(log, scannerClient),
    new CheckDigestTool(checker, state),
    new SubmitLogsTool(hub, checker, state, runDirectory, dryRun: draftMode)
};

Console.WriteLine(draftMode
    ? "DRAFT mode: submit_logs only saves the digest, nothing is sent to /verify."
    : "RUN mode: every submit_logs call that passes the local checks is a real /verify request.");
Console.WriteLine($"Agent: {agentSettings.DefaultModel}, scanner: {scannerSettings.DefaultModel}, token budget: {tokenLimit - tokenSafetyMargin} safe / {tokenLimit} hard.");
Console.WriteLine($"Run directory: {runDirectory} (transcript and every submitted digest).");

var messages = new List<Message>
{
    Message.System(FailureAgentPrompt.SystemPrompt),
    Message.User("Prepare the incident digest for the technicians and get it accepted. Begin.")
};
transcript.Append("system", FailureAgentPrompt.SystemPrompt);
transcript.Append("user", messages[^1].Content!);

const int maxIterations = 40;
var finishRefusals = 0;
var promptTokens = 0;
var completionTokens = 0;

for (var iteration = 1; iteration <= maxIterations && !state.IsComplete; iteration++)
{
    Console.WriteLine($"--- Iteration {iteration} ---");
    var response = await agentClient.CompleteAsync(new LlmRequest { Messages = messages, Tools = tools });
    promptTokens += response.Usage.PromptTokens;
    completionTokens += response.Usage.CompletionTokens;

    if (response.HasToolCalls)
    {
        messages.Add(Message.AssistantToolCalls(response.ContentRaw, response.ToolCalls));
        foreach (var toolCall in response.ToolCalls)
        {
            Console.WriteLine($"  -> {toolCall.FunctionName} {Preview(toolCall.ArgumentsJson, 160)}");
            transcript.Append($"tool call: {toolCall.FunctionName}", toolCall.ArgumentsJson);

            var result = await ExecuteToolAsync(tools, toolCall);
            Console.WriteLine($"  <- {Preview(result, 300)}");
            transcript.Append($"tool result: {toolCall.FunctionName}", result);
            messages.Add(Message.ToolResult(toolCall.Id, result));

            if (state.IsComplete)
                break;
        }
        continue;
    }

    Console.WriteLine(response.Content);
    transcript.Append("assistant", response.Content ?? "");
    messages.Add(Message.Assistant(response.Content ?? ""));

    // The model finished without the run being complete. That is never a valid end state,
    // so push it back to work (bounded, to avoid burning budget on a stuck model).
    if (++finishRefusals > 3)
    {
        Console.WriteLine("Agent kept finishing without a result. Aborting.");
        break;
    }
    var nudge = draftMode
        ? "No digest has passed the checks and been handed to submit_logs yet, so the draft is not ready. Continue working."
        : "No {FLG:...} flag has appeared in any submission result yet, so the technicians have not accepted the digest. " +
          "Apply their latest feedback, re-check the digest and submit again.";
    messages.Add(Message.User(nudge));
    transcript.Append("user", nudge);
}

Console.WriteLine();
Console.WriteLine($"Submissions: {state.SubmissionCount}. Agent tokens: {promptTokens} prompt + {completionTokens} completion.");
Console.WriteLine(state.FlagReceived
    ? $"FLAG RECEIVED: {state.Flag}"
    : state.DraftAccepted
        ? $"Draft ready in '{runDirectory}'. Run with --run to submit for real, or fix it by hand and use --submit <file>."
        : $"No flag received. Check failure-log.jsonl and '{runDirectory}' to diagnose.");

static async Task<string> ExecuteToolAsync(IReadOnlyList<ITool> tools, ToolCall toolCall)
{
    var tool = tools.FirstOrDefault(t => t.Name == toolCall.FunctionName);
    if (tool is null)
        return $"Unknown tool '{toolCall.FunctionName}'.";

    try
    {
        return await tool.ExecuteAsync(toolCall.ArgumentsJson);
    }
    catch (Exception ex)
    {
        return $"Tool '{toolCall.FunctionName}' failed: {ex.Message}";
    }
}

static string Preview(string text, int maxLength)
{
    var singleLine = text.ReplaceLineEndings(" ").Replace("\\n", " | ");
    return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "...";
}
