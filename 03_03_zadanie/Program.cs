using _03_03_zadanie;
using _03_03_zadanie.Agents;
using _03_03_zadanie.Llm;
using _03_03_zadanie.Mission;
using _03_03_zadanie.Reactor;
using _03_03_zadanie.Tools;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("Reactor").Get<TaskSettings>() ?? new TaskSettings();

var testsMode = args.Contains("--tests");
var simulateMode = args.Contains("--simulate");
var boardMode = args.Contains("--board");
var commandIndex = Array.IndexOf(args, "--command");
var runMode = args.Contains("--run");
var offline = args.Contains("--offline");

if (!(testsMode || simulateMode || boardMode || commandIndex >= 0 || runMode))
{
    PrintUsage();
    return;
}

const string logPath = "reactor-log.jsonl";

// ---------------------------------------------------------------------------
// Offline modes. No network, no API key, no robot at risk: the rules that decide
// a move can be changed and re-checked in a second, which is why they are in code.
// ---------------------------------------------------------------------------

if (testsMode)
{
    Environment.ExitCode = MechanicsTestSuite.Run() ? 0 : 1;
    return;
}

if (simulateMode)
{
    var seed = ReadInt("--seed") ?? 0;
    var simulator = new SimulatedReactorApi(seed);

    Console.WriteLine($"Rehearsing the crossing on the simulator (seed {seed}).");
    var crossed = await RehearsalPilot.CrossAsync(simulator);

    Console.WriteLine();
    Console.WriteLine((await simulator.ReadBoardAsync()).Board?.Render());
    Environment.ExitCode = crossed ? 0 : 1;
    return;
}

// ---------------------------------------------------------------------------
// Modes that touch a reactor. --offline swaps the hub for the simulator, so the
// whole agent loop can be rehearsed without spending a command or a robot.
// ---------------------------------------------------------------------------

var mission = new MissionState();
var api = BuildApi();
var session = new ReactorSession(api, mission);

// A reactor left running by an earlier process has a board; reading it first tells this one that
// the run is already open, so it never sends a second 'start' over someone else's progress.
var opening = await session.LookAsync();

if (boardMode)
{
    Console.WriteLine(opening);
    return;
}

if (commandIndex >= 0)
{
    Console.WriteLine(opening);
    Console.WriteLine();

    foreach (var requested in args.Skip(commandIndex + 1).TakeWhile(argument => !argument.StartsWith("--")))
    {
        if (!ReactorCommands.TryParse(requested, out var command))
            throw new InvalidOperationException($"'{requested}' is not a command. Valid: {string.Join(", ", ReactorCommands.All)}.");

        Console.WriteLine($"--- {command.Name()} ---");
        Console.WriteLine(await session.SendAsync(command));
        Console.WriteLine();
    }

    ReportOutcome();
    return;
}

// ---------------------------------------------------------------------------
// The agent loop.
// ---------------------------------------------------------------------------

var agentSettings = configuration.GetSection("Agent").Get<LlmProviderSettings>()
    ?? throw new InvalidOperationException("Missing the 'Agent' configuration section.");

var runDirectory = Path.Combine(settings.CacheDirectory, $"run-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
var transcript = new Transcript(Path.Combine(runDirectory, "transcript.txt"));

var agent = new AgentLoop(
    label: "pilot",
    client: new OpenAiCompatibleLlmClient("Agent", agentSettings),
    systemPrompt: ReactorPrompt.SystemPrompt,
    tools: [new LookTool(session), new SendCommandTool(session)],
    isGoalReached: () => mission.FlagReceived,
    transcript: transcript,
    maxIterations: settings.MaxIterations,
    hooks: new ReactorHooks(session, mission, settings.MaxResets));

Console.WriteLine($"Driving the {api.Name} reactor. Transcript: {transcript.FilePath}");
Console.WriteLine(opening);
Console.WriteLine();

var result = await agent.RunAsync(ReactorPrompt.Task);

Console.WriteLine();
Console.WriteLine($"Iterations: {result.Iterations}, tokens: {result.PromptTokens} in / {result.CompletionTokens} out.");
if (result.Abort is not null)
    Console.WriteLine($"Aborted: {result.Abort}");

ReportOutcome();
return;

IReactorApi BuildApi()
{
    if (offline)
        return new SimulatedReactorApi(ReadInt("--seed") ?? 0);

    var apiKey = configuration["AI_DevsApiKey"] ?? string.Empty;
    return string.IsNullOrWhiteSpace(apiKey)
        ? throw new InvalidOperationException("Missing AI_DevsApiKey — set it in appsettings.Development.json.")
        : new HubReactorApi(settings.HubBaseUrl, apiKey, logPath, settings.MaxCommands);
}

void ReportOutcome()
{
    Console.WriteLine("Commands:");
    Console.WriteLine(mission.RenderHistory());
    Console.WriteLine();
    Console.WriteLine(mission.FlagReceived ? $"FLAG: {mission.Flag}"
        : session.GoalReached ? "The robot reached the goal, but no flag came back."
        : session.Crushed ? "The robot was destroyed."
        : "The robot has not reached the goal.");
}

int? ReadInt(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : null;
}

void PrintUsage()
{
    Console.WriteLine("""
        Usage:
          --tests                      Offline mechanics checks (guard, block travel, parser). No network, no API key.
          --simulate [--seed N]        Rehearse a crossing on the offline simulator with a fixed pilot. No network, no LLM.
          --board [--offline]          Read the board. Does not advance the reactor.
          --command <cmd> [<cmd>...]   Send commands by hand, through the same guard as the agent. Each one ticks the reactor.
          --run [--offline] [--seed N] Run the agent. Without --offline this drives the real reactor at the hub.
        """);
}
