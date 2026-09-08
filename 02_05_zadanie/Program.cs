using _02_05_zadanie.Agents;
using _02_05_zadanie.Drone;
using _02_05_zadanie.Hub;
using _02_05_zadanie.Llm;
using _02_05_zadanie.Map;
using _02_05_zadanie.Mission;
using _02_05_zadanie.Tools;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var mapMode = args.Contains("--map");
var locateMode = args.Contains("--locate");
var manualMode = args.Contains("--manual");
var promptMode = args.Contains("--prompt");
var runMode = args.Contains("--run");
var submitIndex = Array.IndexOf(args, "--submit");
var refresh = args.Contains("--refresh");

if (!(mapMode || locateMode || manualMode || promptMode || runMode || submitIndex >= 0))
{
    Console.WriteLine("S02E05 'drone': fly the mission against the power plant, drop the charge on the dam.");
    Console.WriteLine();
    Console.WriteLine("Reading the ground (no /verify):");
    Console.WriteLine("  dotnet run -- --map          Detect the sector grid, slice the map, count the boosted water.");
    Console.WriteLine("                               No model involved. Sector crops land in drone-cache/.");
    Console.WriteLine("  dotnet run -- --locate       As above, plus the vision model's independent reading.");
    Console.WriteLine("  dotnet run -- --manual       Print the drone manual as the agent will receive it.");
    Console.WriteLine("  dotnet run -- --prompt       Locate the dam, then print the agent's system prompt without flying.");
    Console.WriteLine("  (add --refresh to any of these to re-download instead of using the cache)");
    Console.WriteLine();
    Console.WriteLine("Flying:");
    Console.WriteLine("  dotnet run -- --run          Full agent run. Every send_instructions call is a real /verify request.");
    Console.WriteLine();
    Console.WriteLine("Flying by hand (one /verify request, no LLM, same local checks):");
    Console.WriteLine("  dotnet run -- --submit \"hardReset\" \"setDestinationObject(PWR6132PL)\" \"set(2,4)\" ...");
    return;
}

var aiDevsApiKey = configuration["AI_DevsApiKey"];
if (string.IsNullOrWhiteSpace(aiDevsApiKey))
    throw new InvalidOperationException("Missing 'AI_DevsApiKey'. Set it in appsettings.Development.json.");

var hubBaseUrl = configuration["Drone:HubBaseUrl"] ?? "https://hub.ag3nts.org";
var manualUrl = configuration["Drone:ManualUrl"] ?? "https://hub.ag3nts.org/dane/drone.html";
var targetObjectId = configuration["Drone:TargetObjectId"] ?? "PWR6132PL";
var cacheDirectory = configuration["Drone:CacheDirectory"] ?? "drone-cache";
var maxIterations = configuration.GetValue("Drone:MaxIterations", 12);
var maxSubmissions = configuration.GetValue("Drone:MaxHubSubmissions", 15);

const string logPath = "drone-log.jsonl";
var hub = new HubClient(hubBaseUrl, aiDevsApiKey, cacheDirectory, logPath, maxSubmissions);

if (manualMode)
{
    var printed = await DroneManual.LoadAsync(manualUrl, cacheDirectory, refresh);
    Console.WriteLine(printed.Text);
    return;
}

// ---------------------------------------------------------------------------
// Reading the map. The grid and the boosted water are measured, never estimated —
// this is the one value the mission cannot recover from getting wrong.
// ---------------------------------------------------------------------------

ILlmClient BuildVisionClient() =>
    new OpenAiCompatibleLlmClient("vision", configuration.GetSection("Vision").Get<LlmProviderSettings>()
        ?? throw new InvalidOperationException("Missing 'Vision' provider settings."));

if (mapMode)
{
    var mapPng = await hub.GetMapPngAsync(refresh);
    var analysis = DamLocator.AnalyzeLocally(mapPng);
    await DamLocator.SaveSectorsAsync(analysis.Grid, cacheDirectory);

    Console.WriteLine($"Grid detected: {analysis.Grid.Columns} columns x {analysis.Grid.Rows} rows.");
    foreach (var sector in analysis.Grid.Sectors)
        Console.WriteLine($"  {sector.Label}: {sector.Bounds.Width}x{sector.Bounds.Height} at ({sector.Bounds.X},{sector.Bounds.Y})");

    Console.WriteLine();
    Console.WriteLine(analysis.Water.Render());
    Console.WriteLine();
    Console.WriteLine($"Sector crops written to '{cacheDirectory}'.");
    return;
}

if (locateMode)
{
    var mapPng = await hub.GetMapPngAsync(refresh);
    var dam = await new DamLocator(BuildVisionClient(), cacheDirectory).LocateAsync(mapPng);
    Console.WriteLine();
    Console.WriteLine(dam.Render());
    Console.WriteLine($"The landing instruction for that sector is {DroneInstruction.LandingSector(dam.Column, dam.Row)}.");
    return;
}

// ---------------------------------------------------------------------------
// Flying by hand: no model, but the same guard on the sector.
// ---------------------------------------------------------------------------

if (submitIndex >= 0)
{
    var instructions = args.Skip(submitIndex + 1).Where(a => !a.StartsWith("--")).ToList();
    if (instructions.Count == 0)
        throw new InvalidOperationException("--submit needs at least one instruction, e.g. --submit \"hardReset\" \"getConfig\".");

    var mapPng = await hub.GetMapPngAsync(refresh);
    var analysis = DamLocator.AnalyzeLocally(mapPng);
    if (!analysis.Water.IsConclusive)
        throw new InvalidOperationException($"The dam sector could not be established from the map, so nothing is sent.{Environment.NewLine}{analysis.Water.Render()}");

    var strongest = analysis.Water.Strongest!;
    var handValidator = new InstructionValidator(strongest.Column, strongest.Row, targetObjectId, analysis.Grid.Columns, analysis.Grid.Rows);
    var validation = handValidator.Validate(instructions);
    if (!validation.IsValid)
    {
        Console.WriteLine("Rejected before sending:");
        foreach (var error in validation.Errors)
            Console.WriteLine($"  - {error}");
        return;
    }

    Console.WriteLine($"Sending: [{string.Join(", ", instructions)}]");
    var submission = await hub.SendInstructionsAsync(instructions);
    Console.WriteLine(submission.Render());

    var handState = new MissionState();
    handState.ScanForFlag(submission.Body);
    if (handState.FlagReceived)
        Console.WriteLine($"{Environment.NewLine}Flag: {handState.Flag}");
    return;
}

// ---------------------------------------------------------------------------
// The full run: read the ground, brief the agent, let it work the API.
// ---------------------------------------------------------------------------

var map = await hub.GetMapPngAsync(refresh);
var damLocator = new DamLocator(BuildVisionClient(), cacheDirectory);
var damLocation = await damLocator.LocateAsync(map);
Console.WriteLine();
Console.WriteLine(damLocation.Render());

var manual = await DroneManual.LoadAsync(manualUrl, cacheDirectory, refresh);
Console.WriteLine($"Manual loaded from {manual.SourceUrl} ({manual.Text.Length} characters).");

var systemPrompt = DronePrompt.Build(manual, damLocation, targetObjectId, maxSubmissions);

if (promptMode)
{
    Console.WriteLine();
    Console.WriteLine(systemPrompt);
    Console.WriteLine();
    Console.WriteLine("=== first user message ===");
    Console.WriteLine(DronePrompt.Task(damLocation, targetObjectId));
    return;
}

var grid = DamLocator.AnalyzeLocally(map).Grid;
var validator = new InstructionValidator(damLocation.Column, damLocation.Row, targetObjectId, grid.Columns, grid.Rows);
var state = new MissionState();

var runDirectory = Path.Combine(cacheDirectory, $"run-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
var transcript = new Transcript(Path.Combine(runDirectory, "transcript.txt"));

var agentSettings = configuration.GetSection("Agent").Get<LlmProviderSettings>()
    ?? throw new InvalidOperationException("Missing 'Agent' provider settings.");
var agentClient = new OpenAiCompatibleLlmClient("agent", agentSettings);

var loop = new AgentLoop(
    "operator",
    agentClient,
    systemPrompt,
    [new SendInstructionsTool(hub, validator, state)],
    () => state.FlagReceived,
    transcript,
    maxIterations);

Console.WriteLine();
Console.WriteLine($"Running the operator agent ({agentSettings.DefaultModel}), transcript in {transcript.FilePath}.");
Console.WriteLine();

var result = await loop.RunAsync(DronePrompt.Task(damLocation, targetObjectId), DronePrompt.Nudge);

Console.WriteLine();
Console.WriteLine("=== attempts ===");
Console.WriteLine(state.RenderHistory());
Console.WriteLine();
Console.WriteLine($"Iterations: {result.Iterations}, tokens in/out: {result.PromptTokens}/{result.CompletionTokens}, submissions: {hub.SubmissionCount}.");

if (result.Abort is not null)
    Console.WriteLine($"Stopped: {result.Abort}");

Console.WriteLine(state.FlagReceived
    ? $"{Environment.NewLine}Flag: {state.Flag}"
    : $"{Environment.NewLine}No flag yet. The attempts above are in {logPath}; finish by hand with --submit if the last error is clear.");
