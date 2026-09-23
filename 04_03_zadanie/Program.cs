using System.Text.Json.Nodes;
using _04_03_zadanie;
using _04_03_zadanie.City;
using _04_03_zadanie.Hub;
using _04_03_zadanie.Mission;
using _04_03_zadanie.Tests;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("Domatowo").Get<TaskSettings>() ?? new TaskSettings();
var apiKey = configuration["AI_DevsApiKey"] ?? string.Empty;

const string logPath = "domatowo-log.jsonl";

var testsMode = args.Contains("--tests");
var planMode = args.Contains("--plan");
var helpApiMode = args.Contains("--help-api");
var getMapMode = args.Contains("--get-map");
var stateMode = args.Contains("--state");
var actionIndex = Array.IndexOf(args, "--action");
var nextMode = args.Contains("--next");
var stepMode = args.Contains("--step");
var runMode = args.Contains("--run");

if (!(testsMode || planMode || helpApiMode || getMapMode || stateMode || actionIndex >= 0 || nextMode || stepMode || runMode))
{
    Console.WriteLine("""
        Usage:
          --tests                       run the offline checks: no network, no key
          --plan [--symbol B3]          price every search order from the cached map and tariff, offline
                                        (--map <file>, --costs <file> override the cache)
          --help-api                    ask the domatowo API to describe itself (action "help")
          --get-map [--symbols UL,B3]   fetch the city map (action "getMap"), optionally only the listed symbols
          --state                       read the preview backend: points, units, flags - not an action, costs nothing
          --action <name> [k=v ...]     send one raw action to /verify through the guard, e.g. --action getLogs
                                        or --action create type=transporter passengers=2 (--allow-reset lets reset through)
          --action '<json>'             the same with the answer object given verbatim
          --next                        observe the live board and show the next action; nothing sent
          --step                        observe, choose, vet and send exactly one action
          --run [--max-actions 40]      step until the human is confirmed, something stops the run, or the cap is hit

        --tests and --plan never touch the network. The executor never calls the helicopter: when a scout
        confirms the human it prints the callHelicopter command for the operator to run.
        Raw responses land in the cache directory, every request in domatowo-log.jsonl with the key redacted.
        """);
    return;
}

if (testsMode)
{
    Environment.ExitCode = OfflineTests.Run() ? 0 : 1;
    return;
}

var archive = new ReplyArchive(settings.CacheDirectory);

// ---------------------------------------------------------------------------
// Offline planning. The map and the tariff come from earlier --get-map and
// --action actionCost runs, so the whole search can be priced without a key.
// ---------------------------------------------------------------------------

if (planMode)
{
    var map = LoadCachedMap() ?? throw new InvalidOperationException($"No cached map in {settings.CacheDirectory}; run --get-map first or pass --map <file>.");
    var costs = LoadCosts();
    var symbol = ReadValue("--symbol") ?? settings.TargetSymbol;
    var clusters = ClusterFinder.Find(map, symbol);
    var state = settings.NewOperationState();

    Console.WriteLine(map.Render());
    Console.WriteLine();
    Console.WriteLine($"Tariff: {costs}");
    Console.WriteLine($"Target symbol {symbol} ('{map.KindOfSymbol(symbol)?.Label ?? "unknown"}'): {clusters.Sum(cluster => cluster.Fields.Count)} fields in {clusters.Count} cluster(s)");
    foreach (var cluster in clusters)
        Console.WriteLine($"  {cluster.Name}: fields {string.Join(' ', cluster.Fields)}; stops {string.Join(' ', cluster.Stops)}");
    Console.WriteLine();

    var plans = SearchPlanner.PlanAll(map, clusters, costs, state);
    Console.WriteLine(plans[0].Describe(costs));
    Console.WriteLine();
    Console.WriteLine($"Alternatives ({plans.Count} priced, best first):");
    foreach (var plan in plans.Take(8))
        Console.WriteLine($"  {plan.Summary}");

    Environment.ExitCode = plans[0].FitsBudget ? 0 : 1;
    return;
}

// ---------------------------------------------------------------------------
// Modes that touch the hub.
// ---------------------------------------------------------------------------

if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Missing AI_DevsApiKey - set it in appsettings.Development.json.");

Directory.CreateDirectory(settings.CacheDirectory);
using var hub = new DomatowoClient(settings.HubBaseUrl, apiKey, logPath, settings.RequestTimeoutSeconds, settings.MinSecondsBetweenRequests);

if (helpApiMode)
{
    Console.WriteLine("== help ==");
    Report(await hub.HelpAsync(), "help.json");
}

if (getMapMode)
{
    var symbols = ReadList("--symbols");
    Console.WriteLine(symbols is null ? "== getMap ==" : $"== getMap [{string.Join(", ", symbols)}] ==");
    Report(await hub.GetMapAsync(symbols), symbols is null ? "map.json" : $"map-{string.Join('-', symbols)}.json");
}

if (actionIndex >= 0)
{
    var answer = ParseAction(actionIndex);
    Console.WriteLine($"== action: {answer.ToJsonString()} ==");

    // The guard needs the board as it is now, from the same three free reads the executor uses.
    var operation = await BuildOperationAsync();
    var state = await operation.Operation.ObserveAsync();
    Console.WriteLine($"state: {state}");

    var guard = new ActionGuard(operation.Map, operation.Costs) { AllowReset = args.Contains("--allow-reset") };
    var verdict = guard.Check(answer, state);
    Console.WriteLine($"guard: {verdict}");

    if (!verdict.Accepted)
    {
        Console.WriteLine("Nothing sent.");
        Environment.ExitCode = 2;
        return;
    }

    var reply = await hub.SendActionAsync(answer);
    Report(reply, $"action-{answer["action"]!.GetValue<string>()}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
    if (reply.IsOk)
        operation.Ledger.Record(answer, reply.TryParseJson()!, state);
}

if (nextMode || stepMode || runMode)
{
    var operation = await BuildOperationAsync();
    var outcome = runMode
        ? await operation.Operation.RunAsync(int.TryParse(ReadValue("--max-actions"), out var cap) ? cap : 40)
        : await operation.Operation.StepAsync(send: stepMode);

    Console.WriteLine($"outcome: {outcome}");
    Environment.ExitCode = outcome is StepOutcome.Sent or StepOutcome.Planned or StepOutcome.Finished ? 0 : 1;
}

if (stateMode)
{
    Console.WriteLine("== state (preview backend) ==");
    Report(await hub.PullStateAsync(), "state.json");
}

Console.WriteLine();
Console.WriteLine($"Requests: {hub.ActionsSent} to /verify, {hub.StateReadsSent} free state reads. Log: {logPath}");

// ---------------------------------------------------------------------------

void Report(HubReply reply, string fileName)
{
    Console.WriteLine($"HTTP {reply.Status}  ({reply.Elapsed.TotalMilliseconds:F0} ms)");
    Console.WriteLine(ReplyArchive.Format(reply));
    Console.WriteLine($"Saved to {archive.Save(fileName, reply)}");
    Console.WriteLine();
}

async Task<OperationContext> BuildOperationAsync()
{
    var map = LoadCachedMap();
    if (map is null)
    {
        var mapReply = await hub.GetMapAsync();
        if (!mapReply.IsOk)
            throw new InvalidOperationException($"getMap failed: HTTP {mapReply.Status} {mapReply.Body}");
        archive.Save("map.json", mapReply);
        map = CityMap.Parse(mapReply.Body);
    }

    var costs = LoadCosts();
    var ledger = new OperationLedger(Path.Combine(settings.CacheDirectory, "ledger.json"));
    var clusters = ClusterFinder.Find(map, ReadValue("--symbol") ?? settings.TargetSymbol);
    var transcriptPath = Path.Combine(settings.CacheDirectory, "operation.log");

    void Say(string line)
    {
        Console.WriteLine(line);
        File.AppendAllText(transcriptPath, $"{DateTimeOffset.Now:HH:mm:ss} {line}{Environment.NewLine}");
    }

    return new OperationContext(map, costs, ledger, new Operation(hub, map, costs, settings, ledger, clusters, archive, Say));
}

CityMap? LoadCachedMap()
{
    var path = ReadValue("--map") ?? Path.Combine(settings.CacheDirectory, "map.json");
    return File.Exists(path) ? CityMap.Load(path) : null;
}

// The newest cached actionCost reply wins; without one the prices from the task text apply.
CostTable LoadCosts()
{
    var explicitPath = ReadValue("--costs");
    var path = explicitPath ?? (Directory.Exists(settings.CacheDirectory)
        ? Directory.GetFiles(settings.CacheDirectory, "action-actionCost-*.json").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
        : null);

    if (path is null)
    {
        Console.WriteLine("note: no cached actionCost reply, pricing with the task text's tariff");
        return CostTable.FromTaskText;
    }

    return CostTable.Load(path);
}

string? ReadValue(string flag)
{
    var index = Array.IndexOf(args, flag);
    if (index < 0)
        return null;
    if (index + 1 >= args.Length || args[index + 1].StartsWith("--"))
        throw new InvalidOperationException($"{flag} needs a value.");

    return args[index + 1];
}

IReadOnlyList<string>? ReadList(string flag) =>
    ReadValue(flag)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// Accepts either a verbatim JSON object or the shorthand "name k=v k=v"; numbers and booleans in the
// shorthand become JSON numbers and booleans, so "passengers=2" is sent as an integer.
JsonObject ParseAction(int index)
{
    if (index + 1 >= args.Length || args[index + 1].StartsWith("--"))
        throw new InvalidOperationException("--action needs an action name or a JSON object.");

    var first = args[index + 1];
    if (first.TrimStart().StartsWith('{'))
    {
        var parsed = JsonNode.Parse(first) as JsonObject ?? throw new InvalidOperationException("--action JSON must be an object.");
        if (parsed["action"] is null)
            throw new InvalidOperationException("--action JSON must contain an \"action\" field.");
        return parsed;
    }

    var answer = new JsonObject { ["action"] = first };
    for (var i = index + 2; i < args.Length && !args[i].StartsWith("--"); i++)
    {
        var separator = args[i].IndexOf('=');
        if (separator <= 0)
            throw new InvalidOperationException($"Expected key=value after the action name, got '{args[i]}'.");

        var key = args[i][..separator];
        var value = args[i][(separator + 1)..];
        answer[key] = int.TryParse(value, out var number) ? number
            : bool.TryParse(value, out var flag) ? flag
            : value;
    }

    return answer;
}

/// <summary>What a hub-touching mode needs at hand: the map, the tariff, the crew ledger and the executor built on them.</summary>
sealed record OperationContext(CityMap Map, CostTable Costs, OperationLedger Ledger, Operation Operation);
