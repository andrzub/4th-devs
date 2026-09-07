using _02_04_zadanie.Agents;
using _02_04_zadanie.Hub;
using _02_04_zadanie.Llm;
using _02_04_zadanie.Mailbox;
using _02_04_zadanie.Mission;
using _02_04_zadanie.Tools;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var helpApiMode = args.Contains("--help-api");
var inboxIndex = Array.IndexOf(args, "--inbox");
var searchIndex = Array.IndexOf(args, "--search");
var threadIndex = Array.IndexOf(args, "--thread");
var readIndex = Array.IndexOf(args, "--read");
var resetMode = args.Contains("--reset");
var submitMode = args.Contains("--submit");
var draftMode = args.Contains("--draft");
var runMode = args.Contains("--run");

var anyMode = helpApiMode || resetMode || submitMode || draftMode || runMode
    || inboxIndex >= 0 || searchIndex >= 0 || threadIndex >= 0 || readIndex >= 0;

if (!anyMode)
{
    Console.WriteLine("S02E04 'mailbox': coordinator plus delegated mail researchers.");
    Console.WriteLine();
    Console.WriteLine("Reading the mailbox by hand (no LLM, no /verify):");
    Console.WriteLine("  dotnet run -- --help-api             Print what the zmail API says about itself.");
    Console.WriteLine("  dotnet run -- --inbox [page]         List the mailbox, newest first. Headers only.");
    Console.WriteLine("  dotnet run -- --search \"<query>\"     Search with Gmail-like operators. Headers only.");
    Console.WriteLine("  dotnet run -- --thread <id>          List every message of one conversation.");
    Console.WriteLine("  dotnet run -- --read <id> [<id>...]  Print full message bodies by messageID.");
    Console.WriteLine("  dotnet run -- --reset                Clear the API-side request counter for this key.");
    Console.WriteLine();
    Console.WriteLine("Agents:");
    Console.WriteLine("  dotnet run -- --draft                Full coordinator and researcher run, submission simulated.");
    Console.WriteLine("                                       The answer is written to mailbox-cache/run-*/draft-answer.json.");
    Console.WriteLine("  dotnet run -- --run                  Full run. Every submit_answer call is a real /verify request.");
    Console.WriteLine();
    Console.WriteLine("Finishing by hand (one /verify request, no LLM):");
    Console.WriteLine("  dotnet run -- --submit --date <YYYY-MM-DD> --password <value> --code <SEC-...>");
    return;
}

var aiDevsApiKey = configuration["AI_DevsApiKey"];
if (string.IsNullOrWhiteSpace(aiDevsApiKey))
    throw new InvalidOperationException("Missing 'AI_DevsApiKey'. Set it in appsettings.Development.json.");

var hubBaseUrl = configuration["Mailbox:HubBaseUrl"] ?? "https://hub.ag3nts.org";
var cacheDirectory = configuration["Mailbox:CacheDirectory"] ?? "mailbox-cache";
var maxZmailRequests = configuration.GetValue("Mailbox:MaxZmailRequests", 120);
var maxMessageBodyChars = configuration.GetValue("Mailbox:MaxMessageBodyChars", 6000);
var coordinatorMaxIterations = configuration.GetValue("Mailbox:CoordinatorMaxIterations", 25);
var researcherMaxIterations = configuration.GetValue("Mailbox:ResearcherMaxIterations", 15);

var zmail = new ZmailClient(hubBaseUrl, aiDevsApiKey, "mailbox-log.jsonl", maxZmailRequests);
var store = new MessageStore(zmail);
var hub = new HubClient(hubBaseUrl, aiDevsApiKey, "mailbox-log.jsonl");

// ---------------------------------------------------------------------------
// Manual mailbox access. Same client, same logging, no model involved.
// ---------------------------------------------------------------------------

if (helpApiMode)
{
    Console.WriteLine(await zmail.HelpAsync());
    return;
}

if (resetMode)
{
    Console.WriteLine("Clearing the API-side request counter...");
    Console.WriteLine(await zmail.ResetAsync());
    return;
}

if (inboxIndex >= 0)
{
    var page = int.TryParse(args.ElementAtOrDefault(inboxIndex + 1), out var parsed) ? parsed : 1;
    Console.WriteLine(MailRenderer.RenderHeaders(await zmail.GetInboxAsync(page, 20), $"inbox page {page}"));
    Console.WriteLine(zmail.BudgetLine);
    return;
}

if (searchIndex >= 0)
{
    var query = args.ElementAtOrDefault(searchIndex + 1)
        ?? throw new ArgumentException("Usage: dotnet run -- --search \"<query>\"");
    Console.WriteLine(MailRenderer.RenderHeaders(await zmail.SearchAsync(query, 1, 20), $"search \"{query}\""));
    Console.WriteLine(zmail.BudgetLine);
    return;
}

if (threadIndex >= 0)
{
    var threadId = args.ElementAtOrDefault(threadIndex + 1)
        ?? throw new ArgumentException("Usage: dotnet run -- --thread <id>");
    Console.WriteLine(MailRenderer.RenderHeaders(await zmail.GetThreadAsync(threadId), $"thread {threadId}"));
    Console.WriteLine(zmail.BudgetLine);
    return;
}

if (readIndex >= 0)
{
    var ids = args.Skip(readIndex + 1).TakeWhile(a => !a.StartsWith("--")).ToList();
    if (ids.Count == 0)
        throw new ArgumentException("Usage: dotnet run -- --read <messageID> [<messageID>...]");

    Console.WriteLine(MailRenderer.RenderMessages(await store.GetAsync(ids, refresh: false), int.MaxValue));
    Console.WriteLine(zmail.BudgetLine);
    return;
}

// ---------------------------------------------------------------------------
// Manual submission: the same format checks the agent's tool applies, then one /verify request.
// ---------------------------------------------------------------------------

if (submitMode)
{
    var date = ReadOption(args, "--date");
    var password = ReadOption(args, "--password");
    var code = ReadOption(args, "--code") ?? ReadOption(args, "--confirmation-code");

    var problems = AnswerValidator.Validate(date, password, code);
    if (problems.Count > 0)
    {
        Console.WriteLine("Not submitted:");
        foreach (var problem in problems)
            Console.WriteLine($"  - {problem}");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine("Submitting to /verify...");
    var response = await hub.SubmitAnswerAsync(date!, password!, code!);
    Console.WriteLine(response);

    var manualState = new MissionState();
    manualState.ScanForFlag(response);
    Console.WriteLine(manualState.FlagReceived
        ? $"FLAG RECEIVED: {manualState.Flag}"
        : "No flag in the reply. Read which value the hub names and correct that one.");
    return;
}

// ---------------------------------------------------------------------------
// Coordinator and researchers (--draft / --run).
// ---------------------------------------------------------------------------

var coordinatorSettings = configuration.GetSection("Coordinator").Get<LlmProviderSettings>()
    ?? throw new InvalidOperationException("Missing 'Coordinator' configuration section.");
var researcherSettings = configuration.GetSection("Researcher").Get<LlmProviderSettings>()
    ?? throw new InvalidOperationException("Missing 'Researcher' configuration section.");

var coordinatorClient = new OpenAiCompatibleLlmClient("Coordinator", coordinatorSettings);
var researcherClient = new OpenAiCompatibleLlmClient("Researcher", researcherSettings);

var runDirectory = Path.Combine(cacheDirectory, $"run-{DateTime.Now:yyyyMMdd-HHmmss}");
var transcript = new Transcript(Path.Combine(runDirectory, "coordinator.txt"));
var state = new MissionState();

Console.WriteLine(draftMode
    ? "DRAFT mode: submit_answer only writes the answer to disk, nothing is sent to /verify."
    : "RUN mode: every submit_answer call that passes the format checks is a real /verify request.");
Console.WriteLine($"Coordinator: {coordinatorSettings.DefaultModel}, researchers: {researcherSettings.DefaultModel}.");
Console.WriteLine($"Mailbox budget: {maxZmailRequests} requests. Run directory: {runDirectory} (one transcript per agent).");
Console.WriteLine();

// Step one of the task: let the API describe itself, and hand that description to every
// researcher instead of a paraphrase of its query syntax.
Console.WriteLine("Asking the mailbox API to describe itself...");
var apiHelp = await zmail.HelpAsync();
transcript.Append("zmail help", apiHelp);
Console.WriteLine($"({apiHelp.Length} characters of API reference passed to every researcher.)");
Console.WriteLine();

var runner = new ResearcherRunner(researcherClient, zmail, store, state, apiHelp, runDirectory,
    researcherMaxIterations, maxMessageBodyChars);

var coordinatorTools = new List<ITool>
{
    new DelegateTool(runner, state),
    new MissionStatusTool(state, zmail, runner),
    new SubmitAnswerTool(hub, state, runDirectory, dryRun: draftMode)
};

var coordinator = new AgentLoop("coordinator", coordinatorClient, CoordinatorPrompt.SystemPrompt, coordinatorTools,
    () => state.IsComplete, transcript, coordinatorMaxIterations);

try
{
    var result = await coordinator.RunAsync(
        "Recover the date, the password and the confirmation code from the operator's mailbox, and get the hub to accept them. Begin.",
        _ => draftMode
            ? "No answer has passed the format checks and reached submit_answer yet, so the draft is not ready. Continue working."
            : "The hub has not returned a flag yet, so the answer is not accepted. Read its latest feedback, re-delegate the disputed fact and submit again.");

    if (result.Abort is not null)
        Console.WriteLine($"Run ended early: {result.Abort}");

    Console.WriteLine();
    Console.WriteLine(state.RenderStatus());
    Console.WriteLine();
    Console.WriteLine($"Researchers launched: {runner.Launched}. {zmail.BudgetLine}");
    Console.WriteLine($"Coordinator tokens: {result.PromptTokens} prompt + {result.CompletionTokens} completion.");
}
finally
{
    Console.WriteLine();
    Console.WriteLine(state.FlagReceived
        ? $"FLAG RECEIVED: {state.Flag}"
        : state.DraftAccepted
            ? $"Draft ready in '{runDirectory}'. Run with --run to submit for real, or use --submit with the three values."
            : $"No flag received. Transcripts are in '{runDirectory}', every request is in mailbox-log.jsonl.");
}

static string? ReadOption(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    if (index < 0 || index + 1 >= args.Length)
        return null;

    var value = args[index + 1];
    return value.StartsWith("--") ? null : value;
}
