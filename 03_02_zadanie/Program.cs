using _03_02_zadanie;
using _03_02_zadanie.Agents;
using _03_02_zadanie.Guard;
using _03_02_zadanie.Hub;
using _03_02_zadanie.Llm;
using _03_02_zadanie.Mission;
using _03_02_zadanie.Shell;
using _03_02_zadanie.Tools;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("Firmware").Get<TaskSettings>() ?? new TaskSettings();

var guardTestsMode = args.Contains("--guard-tests");
var guardIndex = Array.IndexOf(args, "--guard");
var helpApiMode = args.Contains("--help-api");
var reconMode = args.Contains("--recon");
var shellIndex = Array.IndexOf(args, "--shell");
var runMode = args.Contains("--run");
var submitCodeIndex = Array.IndexOf(args, "--submit-code");
var submissionEnabled = args.Contains("--submit");

if (!(guardTestsMode || guardIndex >= 0 || helpApiMode || reconMode || shellIndex >= 0 || runMode || submitCodeIndex >= 0))
{
    PrintUsage();
    return;
}

const string logPath = "firmware-log.jsonl";

// ---------------------------------------------------------------------------
// Offline modes. No network, no API key: the blacklist rules can be changed and
// re-checked in a second, which is the point of keeping them in code.
// ---------------------------------------------------------------------------

if (guardTestsMode)
{
    var passed = GuardTestSuite.Run(settings.EffectiveForbiddenPaths);
    Environment.ExitCode = passed ? 0 : 1;
    return;
}

if (guardIndex >= 0)
{
    var command = ArgumentAfter(guardIndex) ?? throw new InvalidOperationException("Pass the command to check: --guard \"cat /etc/passwd\".");
    var offlineGuard = new CommandGuard(settings.EffectiveForbiddenPaths);

    var workingDirectoryIndex = Array.IndexOf(args, "--cwd");
    if (workingDirectoryIndex >= 0 && ArgumentAfter(workingDirectoryIndex) is { } workingDirectory)
        offlineGuard.ObserveWorkingDirectory(workingDirectory);

    var decision = offlineGuard.Inspect(command);
    Console.WriteLine($"{(decision.IsAllowed ? "ALLOWED" : "DENIED ")}  {command}");
    Console.WriteLine($"          {decision.Reason}");
    Console.WriteLine();
    Console.WriteLine(offlineGuard.Describe());
    return;
}

// ---------------------------------------------------------------------------
// Everything below talks to the machine.
// ---------------------------------------------------------------------------

var aiDevsApiKey = configuration["AI_DevsApiKey"];
if (string.IsNullOrWhiteSpace(aiDevsApiKey))
    throw new InvalidOperationException("Missing 'AI_DevsApiKey'. Set it in appsettings.Development.json.");

var hub = new HubClient(settings.HubBaseUrl, aiDevsApiKey, logPath, settings.MaxHubSubmissions);
var mission = new MissionState();

if (submitCodeIndex >= 0)
{
    var code = ArgumentAfter(submitCodeIndex) ?? throw new InvalidOperationException("Pass the code: --submit-code \"ECCS-...\".");
    mission.ScanForCode(code);

    if (mission.ConfirmationCode is null)
        throw new InvalidOperationException($"'{code}' is not a confirmation code: the shape is ECCS- followed by 40 letters and digits.");

    var manualSubmission = await hub.SubmitConfirmationAsync(mission.ConfirmationCode);
    mission.Record(mission.ConfirmationCode, manualSubmission.StatusCode, manualSubmission.Body);
    Console.WriteLine(manualSubmission.Render());
    Console.WriteLine(mission.Flag is { } manualFlag ? $"Flag: {manualFlag}" : "No flag in that response.");
    return;
}

var shellClient = new ShellClient(settings.HubBaseUrl, aiDevsApiKey, logPath, settings.MaxShellRequests);

if (helpApiMode)
{
    var help = await shellClient.SendAsync("help");
    Console.WriteLine(help.RawBody);
    return;
}

var guard = new CommandGuard(settings.EffectiveForbiddenPaths);
var session = new ShellSession(shellClient, guard, mission, settings.MaxOutputCharacters);

if (reconMode || shellIndex >= 0)
{
    await session.BootstrapAsync();

    foreach (var command in ArgumentsAfter(shellIndex))
    {
        Console.WriteLine();
        Console.WriteLine($"$ {command}");
        Console.WriteLine(await session.ExecuteAsync(command));
    }

    Console.WriteLine();
    Console.WriteLine(session.Status());
    ReportCode();
    return;
}

// ---------------------------------------------------------------------------
// The agent run. Recon happens in code first, so the guard already knows every
// blacklist before the model gets its first turn.
// ---------------------------------------------------------------------------

var runDirectory = Path.Combine(settings.CacheDirectory, $"run-{DateTime.Now:yyyyMMdd-HHmmss}");
var transcript = new Transcript(Path.Combine(runDirectory, "transcript.txt"));

Console.WriteLine(submissionEnabled
    ? "Submission is ON: submit_code will send a real /verify request."
    : "Submission is OFF: the run stops once the code has been printed. Add --submit to send it.");
Console.WriteLine();

await session.BootstrapAsync();

var agentSettings = configuration.GetSection("Agent").Get<LlmProviderSettings>()
    ?? throw new InvalidOperationException("Missing 'Agent' provider settings.");

var llm = new OpenAiCompatibleLlmClient("agent", agentSettings);

List<ITool> tools =
[
    new RunCommandTool(session),
    new SubmitCodeTool(hub, mission, submissionEnabled),
    new RebootTool(session, settings.MaxReboots)
];

var systemPrompt = FirmwarePrompt.Build(session.HelpText, settings.BinaryPath, guard.ForbiddenRoots, shellClient.RemainingRequests);

bool IsGoalReached() => submissionEnabled ? mission.FlagReceived : mission.CodeFound;

var loop = new AgentLoop("firmware", llm, systemPrompt, tools, IsGoalReached, transcript, settings.MaxIterations);
var result = await loop.RunAsync(FirmwarePrompt.Task(settings.BinaryPath), FirmwarePrompt.Nudge);

Console.WriteLine();
Console.WriteLine($"Iterations: {result.Iterations}, tokens in/out: {result.PromptTokens}/{result.CompletionTokens}");
Console.WriteLine($"Shell commands sent: {shellClient.RequestCount}, refused by the guard before sending: {guard.DenialCount}");
Console.WriteLine($"Transcript: {transcript.FilePath}");

if (result.Abort is { } abort)
    Console.WriteLine($"Run ended early: {abort}");

ReportCode();

void ReportCode()
{
    if (mission.ConfirmationCode is { } code)
        Console.WriteLine($"Confirmation code: {code}");
    else if (mission.MalformedCandidates.Count > 0)
        Console.WriteLine($"No code in the documented shape. Seen: {string.Join(", ", mission.MalformedCandidates)}");
    else
        Console.WriteLine("No confirmation code was printed.");

    if (mission.Flag is { } flag)
        Console.WriteLine($"Flag: {flag}");
    else if (mission.Submissions.Count > 0)
        Console.WriteLine(mission.RenderHistory());
    else if (mission.ConfirmationCode is { } pending)
        Console.WriteLine($"Nothing has been submitted. To send it: dotnet run -- --submit-code \"{pending}\"");
}

string? ArgumentAfter(int index) => index >= 0 && index + 1 < args.Length ? args[index + 1] : null;

IEnumerable<string> ArgumentsAfter(int index) => index < 0
    ? []
    : args.Skip(index + 1).TakeWhile(argument => !argument.StartsWith("--"));

void PrintUsage()
{
    Console.WriteLine("S03E02 'firmware': get the ECCS cooling controller to start and report its confirmation code.");
    Console.WriteLine();
    Console.WriteLine("Offline, no network and no API key:");
    Console.WriteLine("  dotnet run -- --guard-tests            Run the blacklist guard against its case table.");
    Console.WriteLine("  dotnet run -- --guard \"cat /etc/passwd\" [--cwd /opt]");
    Console.WriteLine("                                         Show what the guard decides about one command.");
    Console.WriteLine();
    Console.WriteLine("Looking around the machine (no /verify):");
    Console.WriteLine("  dotnet run -- --help-api               Print the machine's own command list, raw.");
    Console.WriteLine("  dotnet run -- --recon                  Orient the session: whoami, pwd, and every .gitignore.");
    Console.WriteLine("  dotnet run -- --shell \"ls\" \"cat settings.ini\"");
    Console.WriteLine("                                         Recon, then run commands by hand through the same guard.");
    Console.WriteLine();
    Console.WriteLine("The agent:");
    Console.WriteLine("  dotnet run -- --run                    Full run. Stops once the code is printed, sends nothing.");
    Console.WriteLine("  dotnet run -- --run --submit           As above, and submit_code makes a real /verify request.");
    Console.WriteLine();
    Console.WriteLine("Submitting by hand (one /verify request, no LLM):");
    Console.WriteLine("  dotnet run -- --submit-code \"ECCS-...\"");
}
