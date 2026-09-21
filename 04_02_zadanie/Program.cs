using _04_02_zadanie;
using _04_02_zadanie.Analysis;
using _04_02_zadanie.Hub;
using _04_02_zadanie.Mission;
using _04_02_zadanie.Tests;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("WindPower").Get<TaskSettings>() ?? new TaskSettings();
var apiKey = configuration["AI_DevsApiKey"] ?? string.Empty;

const string logPath = "windpower-log.jsonl";

var helpApiMode = args.Contains("--help-api");
var docMode = args.Contains("--doc");
var probeMode = args.Contains("--probe");
var reconMode = args.Contains("--recon");
var planMode = args.Contains("--plan");
var rehearsalMode = args.Contains("--rehearsal");
var runMode = args.Contains("--run");
var testsMode = args.Contains("--tests");

if (!(testsMode || helpApiMode || docMode || probeMode || reconMode || planMode || rehearsalMode || runMode))
{
    Console.WriteLine("""
        Usage:
          --tests      run the offline checks: no network, no key
          --plan       work out the schedule from the cached reports, offline
          --help-api   ask the windpower API to describe itself (action "help")
          --doc        fetch the documentation without opening a service window
          --probe      order every report WITHOUT a window, to see whether the queue works outside it
          --recon      open a window, order every report, drain the queue, configure nothing
          --rehearsal  open a window and plan from this session's data, stopping before config and done
          --run        the whole errand: start, plan, sign, config, done

        Only "start" opens the service window; --tests, --plan, --help-api and --doc leave the clock alone.
        """);
    return;
}

if (testsMode)
{
    Environment.ExitCode = OfflineTests.Run() ? 0 : 1;
    return;
}

var cache = new DataCache(settings.CacheDirectory);
cache.AdoptFromReconRuns();

if (planMode)
{
    var turbine = cache.LoadTurbine();
    var forecast = cache.LoadForecast();
    var deficit = cache.LoadDeficit();
    var plan = SchedulePlanner.Plan(forecast, turbine, deficit);

    Console.WriteLine($"Turbine: {turbine.RatedPowerKw} kW rated, generates from {turbine.MinOperationalWindMs} m/s, damaged from {turbine.CutoffWindMs} m/s.");
    Console.WriteLine($"Plant deficit: {deficit} kW.");
    Console.WriteLine($"Forecast: {forecast.Entries.Count} readings every {forecast.IntervalHours}h, {forecast.Entries[0].Timestamp:yyyy-MM-dd HH:mm} to {forecast.Entries[^1].Timestamp:yyyy-MM-dd HH:mm}.");
    Console.WriteLine();

    Console.WriteLine("Schedule:");
    foreach (var point in plan.Points)
        Console.WriteLine($"  {point}");

    Console.WriteLine();
    foreach (var note in plan.Notes)
        Console.WriteLine($"note: {note}");

    var verdict = ScheduleValidator.Validate(plan, forecast, turbine);
    Console.WriteLine();
    Console.WriteLine(verdict.Accepted ? "Checks passed." : "Checks failed:");
    foreach (var problem in verdict.Problems)
        Console.WriteLine($"  - {problem}");

    return;
}

if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Missing AI_DevsApiKey - set it in appsettings.Development.json.");

using var hub = new WindPowerClient(settings.HubBaseUrl, apiKey, logPath, settings.RequestTimeoutSeconds);

if (helpApiMode)
{
    var reply = await hub.HelpAsync();
    Console.WriteLine($"HTTP {reply.Status}  ({reply.Elapsed.TotalMilliseconds:F0} ms)");
    Console.WriteLine(reply.Body);
    return;
}

if (docMode)
{
    var reply = await hub.GetAsync("documentation");
    Console.WriteLine($"HTTP {reply.Status}  ({reply.Elapsed.TotalMilliseconds:F0} ms)");
    Console.WriteLine(reply.Body);

    cache.Save(cache.DocumentationPath, reply.Body);
    Console.WriteLine();
    Console.WriteLine($"Saved to {cache.DocumentationPath}");
    return;
}

if (probeMode || reconMode)
{
    var reconLabel = probeMode ? "probe" : "recon";
    var folder = Path.Combine(settings.CacheDirectory, $"{reconLabel}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
    await Recon.RunAsync(hub, folder, TimeSpan.FromSeconds(38), openWindow: reconMode);
    return;
}

// The forecast and the deficit are regenerated per session, so the only thing carried in from
// outside the window is the turbine's own documentation - plus the last known deficit, kept purely
// as a fallback for the case where this session's plant report never arrives.
var turbineModel = cache.LoadTurbine();
var lastKnownDeficit = cache.TryLoadDeficit();

var mode = runMode ? WindowMode.Full : WindowMode.Rehearsal;
var label = runMode ? "run" : "rehearsal";
var runDirectory = Path.Combine(settings.CacheDirectory, $"{label}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");

var window = new WindowRun(hub, turbineModel, runDirectory);
var finished = await window.RunAsync(mode, lastKnownDeficit, TimeSpan.FromSeconds(settings.WindowBudgetSeconds));

Console.WriteLine();
Console.WriteLine($"Raw responses: {runDirectory}");
Environment.ExitCode = finished ? 0 : 1;
