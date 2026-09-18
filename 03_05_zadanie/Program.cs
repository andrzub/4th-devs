using System.Text.Json;
using _03_05_zadanie;
using _03_05_zadanie.Agents;
using _03_05_zadanie.Hub;
using _03_05_zadanie.Llm;
using _03_05_zadanie.Mission;
using _03_05_zadanie.Tests;
using _03_05_zadanie.Tools;
using _03_05_zadanie.World;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("SaveThem").Get<TaskSettings>() ?? new TaskSettings();

var testsMode = args.Contains("--tests");
var toolsIndex = Array.IndexOf(args, "--tools");
var bootstrapMode = args.Contains("--bootstrap");
var askIndex = Array.IndexOf(args, "--ask");
var planMode = args.Contains("--plan");
var checkIndex = Array.IndexOf(args, "--check");
var previewMode = args.Contains("--preview");
var submitRouteIndex = Array.IndexOf(args, "--submit-route");
var runMode = args.Contains("--run");
var submitEnabled = args.Contains("--submit");
var worldPath = ReadValue("--world") ?? Path.Combine(settings.CacheDirectory, "world.json");

if (!(testsMode || toolsIndex >= 0 || bootstrapMode || askIndex >= 0 || planMode || checkIndex >= 0 || previewMode || submitRouteIndex >= 0 || runMode))
{
    PrintUsage();
    return;
}

const string logPath = "savethem-log.jsonl";

// ---------------------------------------------------------------------------
// Offline modes. No network, no API key: the rules that judge a route can be
// changed and re-checked in a second, which is why they live in code.
// ---------------------------------------------------------------------------

if (testsMode)
{
    Environment.ExitCode = OfflineTests.Run() ? 0 : 1;
    return;
}

if (planMode || checkIndex >= 0)
{
    var world = LoadWorld();

    if (planMode)
    {
        Console.WriteLine(world.Render());
        Console.WriteLine();
        Console.WriteLine(RoutePlanner.Plan(world).Render());
    }

    if (checkIndex >= 0)
    {
        var outcome = RouteSimulator.Run(world, ReadRoute(checkIndex));
        Console.WriteLine();
        Console.WriteLine(outcome.Report);
        Environment.ExitCode = outcome.IsValid ? 0 : 1;
    }

    return;
}

// ---------------------------------------------------------------------------
// Modes that touch the hub.
// ---------------------------------------------------------------------------

var apiKey = configuration["AI_DevsApiKey"] ?? string.Empty;
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Missing AI_DevsApiKey — set it in appsettings.Development.json.");

var hub = new HubClient(settings.HubBaseUrl, apiKey, logPath, settings.MaxHubRequests, settings.MinSecondsBetweenHubRequests);
var registry = new ToolRegistry();
var mission = new MissionState();
// The agent only reaches /verify with --submit; a route given by hand is already an explicit decision.
var expedition = new Expedition(hub, registry, mission, settings, submissionEnabled: submitEnabled || submitRouteIndex >= 0);

if (toolsIndex >= 0)
{
    Console.WriteLine(await expedition.SearchToolsAsync(ReadArgument(toolsIndex, "--tools needs a query.")));
    return;
}

if (askIndex >= 0)
{
    var target = ReadArgument(askIndex, "--ask needs a tool name or path and a query.");
    var query = args.Length > askIndex + 2 ? args[askIndex + 2] : throw new InvalidOperationException("--ask needs a query.");

    if (target.StartsWith('/'))
    {
        var direct = await hub.AskToolAsync(target, query);
        Console.WriteLine(direct.Body);
        return;
    }

    // The registry starts empty in a fresh process, so a tool named by hand is looked up first.
    await expedition.SearchToolsAsync(target);
    Console.WriteLine(await expedition.AskToolAsync(target, query));
    return;
}

if (bootstrapMode)
{
    Console.WriteLine(await expedition.BootstrapAsync());
    return;
}

if (previewMode)
{
    Console.WriteLine(await expedition.ReadPreviewAsync());
    return;
}

if (submitRouteIndex >= 0)
{
    expedition.RegisterWorld(ReadWorldJson());
    Console.WriteLine(await expedition.SubmitAsync(ReadRoute(submitRouteIndex)));
    ReportOutcome();
    return;
}

// ---------------------------------------------------------------------------
// The agent loop. Without --submit nothing reaches /verify: routes are replayed
// against the registered rules and the run ends on one the guard accepts.
// ---------------------------------------------------------------------------

var agentSettings = configuration.GetSection("Agent").Get<LlmProviderSettings>()
    ?? throw new InvalidOperationException("Missing the 'Agent' configuration section.");

var runDirectory = Path.Combine(settings.CacheDirectory, $"run-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
var transcript = new Transcript(Path.Combine(runDirectory, "transcript.txt"));

var agent = new AgentLoop(
    label: "planner",
    client: new OpenAiCompatibleLlmClient("Agent", agentSettings),
    systemPrompt: ExpeditionPrompt.SystemPrompt,
    tools: [
        new SearchToolsTool(expedition),
        new AskToolTool(expedition),
        new RegisterWorldTool(expedition),
        new PlanRouteTool(expedition),
        new SubmitRouteTool(expedition)
    ],
    isGoalReached: () => expedition.IsSettled,
    transcript: transcript,
    maxIterations: settings.MaxIterations,
    hooks: new ExpeditionHooks(expedition));

Console.WriteLine($"Planning the courier's route{(submitEnabled ? "" : " (submissions off)")}. Transcript: {transcript.FilePath}");

// Which tools exist is settled before the first turn: the registry matches on keywords, and letting
// the agent guess them costs iterations and requests without teaching it anything it could not be told.
var registryDigest = await expedition.BootstrapAsync();
Console.WriteLine(registryDigest);
Console.WriteLine();

var result = await agent.RunAsync(ExpeditionPrompt.BuildTask(registryDigest));

if (expedition.World is not null)
{
    // Keeping the rules the agent registered lets --plan and --check rerun the same world offline.
    Directory.CreateDirectory(runDirectory);
    File.WriteAllText(Path.Combine(runDirectory, "world.json"), JsonSerializer.Serialize(new
    {
        map = expedition.World.Map.RawRows,
        fuel_budget = expedition.World.FuelBudget,
        food_budget = expedition.World.FoodBudget,
        tree_extra_fuel = expedition.World.TreeExtraFuel,
        walk_mode = expedition.World.WalkMode,
        vehicles = expedition.World.Vehicles.Select(vehicle => new
        {
            name = vehicle.Name,
            fuel_per_move = vehicle.FuelPerMove,
            food_per_move = vehicle.FoodPerMove,
            can_enter_water = vehicle.CanEnterWater,
            selectable_at_start = vehicle.SelectableAtStart
        })
    }, new JsonSerializerOptions { WriteIndented = true }));
}

Console.WriteLine();
Console.WriteLine($"Iterations: {result.Iterations}  tokens: {result.PromptTokens} in / {result.CompletionTokens} out  hub calls: {hub.RequestsSent}");
if (result.Abort is not null)
    Console.WriteLine($"Run aborted: {result.Abort}");

ReportOutcome();
return;

// ---------------------------------------------------------------------------

void ReportOutcome()
{
    Console.WriteLine();
    Console.WriteLine(mission.RenderHistory());

    if (mission.FlagReceived)
        Console.WriteLine($"{Environment.NewLine}Flag: {mission.Flag}");
    else if (!submitEnabled)
        Console.WriteLine($"{Environment.NewLine}Nothing was sent to /verify. Rerun with --submit to send the route.");
}

WorldModel LoadWorld()
{
    if (!WorldModel.TryParse(ReadWorldJson(), out var world, out var error))
        throw new InvalidOperationException($"{worldPath} does not describe a usable world: {error}");

    return world!;
}

JsonElement ReadWorldJson()
{
    if (!File.Exists(worldPath))
        throw new FileNotFoundException($"No world file at {worldPath}. Point --world at one, or let a run write it.", worldPath);

    return JsonDocument.Parse(File.ReadAllText(worldPath)).RootElement.Clone();
}

List<string> ReadRoute(int index) =>
    RouteInstructions.Split(args.Skip(index + 1).TakeWhile(argument => !argument.StartsWith("--")));

string ReadArgument(int index, string message) =>
    args.Length > index + 1 && !args[index + 1].StartsWith("--") ? args[index + 1] : throw new InvalidOperationException(message);

string? ReadValue(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && args.Length > index + 1 ? args[index + 1] : null;
}

void PrintUsage()
{
    Console.WriteLine("""
        S03E05 "savethem" — plans the courier's route to Skolwin.

        Offline (no network, no API key):
          --tests                                 run the rule checks
          --plan [--world <file>]                 plan every departure mode against a registered world
          --check "<route>" [--world <file>]      replay one route against that world

        Against the hub (no /verify):
          --bootstrap                             the opening sweep of the registry the agent starts from
          --tools "<query>"                       search the tool registry
          --ask <tool|/path> "<query>"            ask one tool
          --preview                               read the preview state of the last submitted route

        Agent:
          --run                                   full loop, routes checked but never sent
          --run --submit                          full loop, the accepted route goes to /verify
          --submit-route <mode> <move>...         send one route by hand through the same guard

        Routes read as: <mode> up|down|left|right|dismount ...
        """);
}
