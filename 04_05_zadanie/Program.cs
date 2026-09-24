using _04_05_zadanie;
using _04_05_zadanie.Agents;
using _04_05_zadanie.Hub;
using _04_05_zadanie.Llm;
using _04_05_zadanie.Mission;
using _04_05_zadanie.Tests;
using _04_05_zadanie.Tools;
using _04_05_zadanie.Warehouse;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("Foodwarehouse").Get<TaskSettings>() ?? new TaskSettings();
var apiKey = configuration["AI_DevsApiKey"] ?? string.Empty;

const string logPath = "foodwarehouse-log.jsonl";

var testsMode = args.Contains("--tests");
var demandMode = args.Contains("--demand");
var fetchMode = args.Contains("--fetch");
var validateIndex = Array.IndexOf(args, "--validate");
var promptMode = args.Contains("--prompt");
var helpApiMode = args.Contains("--help-api");
var queryIndex = Array.IndexOf(args, "--query");
var ordersIndex = Array.IndexOf(args, "--orders");
var runMode = args.Contains("--run");
var verifyIndex = Array.IndexOf(args, "--verify");
var submitIndex = Array.IndexOf(args, "--submit");
var resetMode = args.Contains("--reset");
var doneMode = args.Contains("--done");

if (!(testsMode || demandMode || fetchMode || validateIndex >= 0 || promptMode || helpApiMode || queryIndex >= 0 || ordersIndex >= 0 || runMode
      || verifyIndex >= 0 || submitIndex >= 0 || resetMode || doneMode))
{
    PrintUsage();
    return;
}

// ---------------------------------------------------------------------------
// Offline modes. No network, no key.
// ---------------------------------------------------------------------------

if (testsMode)
{
    Environment.ExitCode = OfflineTests.Run() ? 0 : 1;
    return;
}

if (fetchMode)
{
    using var http = new HttpClient();
    var json = await http.GetStringAsync(settings.DemandUrl);
    Demand.Parse(json);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settings.DemandFile))!);
    File.WriteAllText(settings.DemandFile, json);
    Console.WriteLine($"Demand file saved to {Path.GetFullPath(settings.DemandFile)} ({json.Length} chars).");
}

var demand = Demand.Load(ResolvePath(settings.DemandFile));

if (demandMode)
    Console.WriteLine(demand.Render());

if (validateIndex >= 0)
{
    // The plan is judged against the observations saved by the same run, so the check needs no network.
    var planPath = ValueAfter(validateIndex) ?? throw new ArgumentException("--validate needs the path of a plan.json.");
    var observationsPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(planPath))!, "observations.json");
    if (!File.Exists(observationsPath))
        throw new FileNotFoundException($"No observations.json next to the plan ({observationsPath}); --validate needs the facts the run read from the database.");

    var plan = OrderPlan.FromJson(File.ReadAllText(planPath));
    var observations = Observations.FromJson(File.ReadAllText(observationsPath));
    var report = PlanValidator.Validate(plan, demand, observations);

    Console.WriteLine(plan.Render());
    Console.WriteLine();
    Console.WriteLine(observations.RenderSummary());
    Console.WriteLine();
    Console.WriteLine(report.Render());
    Environment.ExitCode = report.IsValid ? 0 : 1;
}

if (promptMode)
{
    // The API help comes from the cache when a --help-api run left one, so the prompt can be read offline.
    var cachedHelp = Path.Combine(settings.CacheDirectory, "help.json");
    var help = File.Exists(cachedHelp) ? File.ReadAllText(cachedHelp) : "(the API help is pasted here at run time; run --help-api once to see it in this preview)";
    var preview = new MissionState(demand);
    Console.WriteLine("=== system prompt ===");
    Console.WriteLine(WarehousePrompt.Build(help));
    Console.WriteLine();
    Console.WriteLine("=== task ===");
    Console.WriteLine(WarehousePrompt.BuildTask(preview));
}

if (!(helpApiMode || queryIndex >= 0 || ordersIndex >= 0 || runMode || verifyIndex >= 0 || submitIndex >= 0 || resetMode || doneMode))
    return;

// ---------------------------------------------------------------------------
// Hub modes. Every call goes through the same client, is counted and logged.
// ---------------------------------------------------------------------------

if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Missing AI_DevsApiKey — set it in appsettings.Development.json.");

Directory.CreateDirectory(settings.CacheDirectory);
var hub = new FoodwarehouseClient(settings.HubBaseUrl, apiKey, logPath, settings.MaxHubRequests, settings.MinSecondsBetweenHubRequests);

if (helpApiMode)
{
    var reply = await hub.HelpAsync();
    Console.WriteLine(reply.Describe());

    // Kept on disk so the API's own description can be reread without spending another call.
    var helpPath = Path.Combine(settings.CacheDirectory, "help.json");
    File.WriteAllText(helpPath, reply.Body);
    Console.WriteLine($"{Environment.NewLine}Saved to {helpPath}.");
}

if (queryIndex >= 0)
{
    var query = ValueAfter(queryIndex) ?? throw new ArgumentException("--query needs the query text.");
    var verdict = QueryGuard.Evaluate(query);
    if (!verdict.Allowed)
    {
        Console.WriteLine($"Refused by the guard: {verdict.Reason}");
        Environment.ExitCode = 1;
        return;
    }

    var reply = await hub.QueryAsync(verdict.Query);
    Console.WriteLine($"=== {verdict.Query} ===");
    Console.WriteLine(reply.IsSuccess ? QueryResult.Parse(reply.Body).Render() : reply.Describe());
}

if (ordersIndex >= 0)
{
    var id = ValueAfter(ordersIndex);
    var reply = await hub.GetOrdersAsync(id);
    Console.WriteLine($"=== orders get{(id is null ? string.Empty : $" {id}")} ===");
    Console.WriteLine(reply.IsSuccess ? OrderBook.Render(OrderBook.Parse(reply.Body)) : reply.Describe());
}

// ---------------------------------------------------------------------------
// The agent loop. It reads the database and the order list through /verify
// and registers a plan; it never creates, changes or deletes an order. The
// plan is saved for --submit.
// ---------------------------------------------------------------------------

if (runMode)
{
    var agentSettings = configuration.GetSection("Agent").Get<LlmProviderSettings>()
        ?? throw new InvalidOperationException("Missing the 'Agent' configuration section.");

    var helpReply = await hub.HelpAsync();
    if (!helpReply.IsSuccess)
        throw new InvalidOperationException($"The API help could not be read: {helpReply.Describe()}");
    File.WriteAllText(Path.Combine(settings.CacheDirectory, "help.json"), helpReply.Body);

    var runDirectory = Path.Combine(settings.CacheDirectory, $"run-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
    var state = new MissionState(demand);
    var transcript = new Transcript(Path.Combine(runDirectory, "transcript.txt"));

    var agent = new AgentLoop(
        label: "dispatcher",
        client: new OpenAiCompatibleLlmClient("Agent", agentSettings),
        systemPrompt: WarehousePrompt.Build(helpReply.Body),
        tools: [
            new QueryDatabaseTool(state, hub),
            new ListOrdersTool(state, hub),
            new RegisterOrdersTool(state),
            new CheckPlanTool(state)
        ],
        isGoalReached: () => state.Accepted,
        transcript: transcript,
        maxIterations: settings.MaxIterations,
        hooks: new WarehouseHooks(state, hub),
        maxNudges: 4);

    Console.WriteLine($"Planning the orders (read-only; no order is created). Transcript: {transcript.FilePath}");
    Console.WriteLine();

    var result = await agent.RunAsync(WarehousePrompt.BuildTask(state));

    // The final validation is the run's own, whether or not the agent asked for one.
    var finalReport = state.Check();
    var planPath = Path.Combine(runDirectory, "plan.json");
    File.WriteAllText(planPath, state.Plan.ToJson());
    File.WriteAllText(Path.Combine(runDirectory, "observations.json"), state.Observations.ToJson());
    File.WriteAllText(Path.Combine(runDirectory, "validation.txt"), finalReport.Render());

    Console.WriteLine();
    Console.WriteLine(state.Plan.Render());
    Console.WriteLine();
    Console.WriteLine(finalReport.Render());
    Console.WriteLine();
    Console.WriteLine($"Iterations: {result.Iterations}  tokens: {result.PromptTokens} in / {result.CompletionTokens} out  queries: {state.Queries} ({state.QueryRefusals} refused)  registrations: {state.Registrations} ({state.RegistrationRefusals} refused)  hub requests: {hub.RequestsSent}");
    if (result.Abort is not null)
        Console.WriteLine($"Run aborted: {result.Abort}");

    Console.WriteLine();
    Console.WriteLine(finalReport.IsValid
        ? $"Plan saved to {planPath}. Place the orders with: --submit \"{planPath}\""
        : $"Plan saved to {planPath} but it is not valid; fix it (or rerun) before --submit.");
}

// ---------------------------------------------------------------------------
// Execution. The plan is verified against the live database first; then the
// orders are placed by code. done is a separate, explicit step.
// ---------------------------------------------------------------------------

if (verifyIndex >= 0 || submitIndex >= 0)
{
    var planPath = ValueAfter(verifyIndex >= 0 ? verifyIndex : submitIndex) ?? throw new ArgumentException("--verify/--submit need the path of a plan.json.");
    var plan = OrderPlan.FromJson(File.ReadAllText(planPath));

    Console.WriteLine(plan.Render());
    Console.WriteLine();
    Console.WriteLine("=== live verification ===");
    var (observations, report) = await PlanVerifier.VerifyLiveAsync(hub, plan, demand);
    Console.WriteLine(observations.RenderSummary());
    Console.WriteLine(report.Render());
    Console.WriteLine();

    if (!report.IsValid)
    {
        Console.WriteLine("The plan does not hold against the live database; nothing was placed.");
        Environment.ExitCode = 1;
        return;
    }

    if (submitIndex < 0)
    {
        Console.WriteLine($"The plan holds. Place the orders with: --submit \"{planPath}\"");
        return;
    }

    var executor = new OrderExecutor(hub, demand);
    var placed = await executor.ExecuteAsync(plan);
    Console.WriteLine();
    Console.WriteLine(placed
        ? $"Orders placed and verified ({hub.RequestsSent} hub requests). Ask for the verdict with: --done"
        : $"Stopped before done ({hub.RequestsSent} hub requests). Check the orders with --orders and the log.");
    Environment.ExitCode = placed ? 0 : 1;
    return;
}

if (resetMode)
{
    Console.WriteLine("=== reset ===");
    Console.WriteLine((await hub.ResetAsync()).Describe());
}

if (doneMode)
{
    Console.WriteLine("=== done ===");
    var doneReply = await hub.DoneAsync();
    Console.WriteLine(doneReply.Describe());

    var flag = FlagDetector.Find(doneReply.Body);
    Console.WriteLine();
    Console.WriteLine(flag is not null ? $"Flag: {flag}" : "No flag in the reply. Read the message above, adjust the plan and submit again.");
    Environment.ExitCode = flag is not null ? 0 : 1;
}

return;

// ---------------------------------------------------------------------------

string? ValueAfter(int index) =>
    index + 1 < args.Length && !args[index + 1].StartsWith("--") ? args[index + 1] : null;

// Files under the project directory win over the copies next to the binary, so --fetch is seen without a rebuild.
static string ResolvePath(string relative) =>
    File.Exists(relative) ? Path.GetFullPath(relative) : Path.Combine(AppContext.BaseDirectory, relative);

static void PrintUsage()
{
    Console.WriteLine("""
        Usage:
          Offline (no network, no key):
            --tests                         checks of the query guard, result paging, the demand file and order comparison
            --demand                        print what each city needs, as read from data/food4cities.json
            --validate <plan.json>          judge a saved plan against the observations.json saved next to it
            --prompt                        print the system prompt and the task the agent will receive

          Data (network, no key):
            --fetch                         download the demand file from the hub into data/food4cities.json

          Hub (each is a call to /verify, read-only):
            --help-api                      the foodwarehouse API's own help, saved to foodwarehouse-cache/help.json
            --query "<sql>"                 run a read-only query through the guard (SELECT, SHOW TABLES, .schema, ...)
            --orders [id]                   list the orders as the hub sees them

          Agent (model key + hub key; reads the database through /verify, creates no order):
            --run                           plan the orders; saves plan.json, observations.json, validation.txt and the
                                            transcript under foodwarehouse-cache/run-<date>/

          Execution (hub key; each step is one or more calls to /verify):
            --verify <plan.json>            re-read the rows the plan relies on and judge it; places nothing
            --submit <plan.json>            verify, then reset + clear + signature/create/append per city + final check
            --reset                         restore the warehouse's seeded orders
            --done                          ask the warehouse for its verdict; prints the flag when it accepts
        """);
}
