using System.Text;
using _05_02_zadanie;
using _05_02_zadanie.Call;
using _05_02_zadanie.Hub;
using _05_02_zadanie.Llm;
using _05_02_zadanie.Tests;
using _05_02_zadanie.Voice;
using Microsoft.Extensions.Configuration;

Console.OutputEncoding = Encoding.UTF8;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("Phonecall").Get<TaskSettings>() ?? new TaskSettings();
var voiceSettings = configuration.GetSection("Voice").Get<VoiceSettings>() ?? new VoiceSettings();
var apiKey = configuration["AI_DevsApiKey"] ?? string.Empty;

const string logPath = "phonecall-log.jsonl";

var testsMode = args.Contains("--tests");
var voicesMode = args.Contains("--voices");
var sayIndex = Array.IndexOf(args, "--say");
var probeMode = args.Contains("--probe");
var rehearsalIndex = Array.IndexOf(args, "--rehearsal");
var callMode = args.Contains("--call");
var stepByStep = args.Contains("--step");

if (!testsMode && !voicesMode && sayIndex < 0 && !probeMode && rehearsalIndex < 0 && !callMode)
{
    PrintUsage();
    return;
}

if (testsMode)
{
    Environment.ExitCode = OfflineTests.Run() ? 0 : 1;
    return;
}

var studio = new VoiceStudio(new OpenAiAudioClient(voiceSettings), voiceSettings, Path.Combine(settings.CacheDirectory, "voice"));

if (voicesMode)
{
    var bank = new VoiceBank(studio, voiceSettings, Path.Combine(settings.CacheDirectory, "voice-bank.json"));
    Console.WriteLine($"Voice {voiceSettings.SpeechModel} / {voiceSettings.Voice}; listeners: {string.Join(", ", voiceSettings.CheckModels)}");
    Console.WriteLine();

    var entries = await bank.PrepareAsync(new CallScript(settings).AllLines(), PrintTake);
    var passed = entries.Count(entry => entry.Passed);
    var retakes = entries.Sum(entry => entry.Take - 1);
    Console.WriteLine($"{passed}/{entries.Count} lines heard correctly by every listener, {retakes} retake(s). Manifest: {Path.GetFullPath(bank.ManifestPath)}");
    Environment.ExitCode = passed == entries.Count ? 0 : 1;
}

if (sayIndex >= 0)
{
    var text = sayIndex + 1 < args.Length ? args[sayIndex + 1] : throw new ArgumentException("--say needs the text to speak, in quotes.");
    var recording = await studio.RecordAsync(text);
    Console.WriteLine($"{recording.Audio.Length:N0} B -> {Path.GetFullPath(recording.Path)}{(recording.FromCache ? " (cached)" : "")}");
    foreach (var model in voiceSettings.CheckModels)
        Console.WriteLine($"  {model,-24} \"{await studio.HearAsync(recording.Audio, "line.mp3", model)}\"");
}

if (probeMode)
{
    // Reconnaissance: "start" is the only request this mode can send, so nothing is ever said to the operator.
    var client = new PhoneClient(settings.HubBaseUrl, RequireHubKey(), logPath);
    var recorder = CallRecorder.CreateRun(settings.CacheDirectory);
    var script = new CallScript(settings);

    Console.WriteLine($"start sent at {DateTime.Now:HH:mm:ss}");
    var reply = await client.StartAsync();
    recorder.SaveReply(0, "start", reply);
    Console.WriteLine(reply.Preview());

    var recordings = reply.FindAudio();
    for (var index = 0; index < recordings.Count; index++)
    {
        var audio = recordings[index];
        var path = recorder.SaveOperatorAudio(0, index, audio);
        var heard = await studio.HearAsync(audio.Bytes, Path.GetFileName(path), voiceSettings.TranscriptionModel, script.VocabularyHint);
        recorder.AppendTranscript("operator", heard);
        Console.WriteLine($"operator audio at {audio.JsonPath}: {audio.Bytes.Length:N0} B {audio.Extension} -> {voiceSettings.TranscriptionModel}: \"{heard}\"");
    }

    if (recordings.Count == 0)
        Console.WriteLine("No audio in the reply.");
    if (reply.Flag is not null)
        Console.WriteLine("The reply carries a flag.");

    Console.WriteLine($"Run: {Path.GetFullPath(recorder.RunDirectory)}; {client.RequestsSent} request(s) to the hub.");
}

if (rehearsalIndex >= 0 || callMode)
{
    var script = new CallScript(settings);
    var bank = new VoiceBank(studio, voiceSettings, Path.Combine(settings.CacheDirectory, "voice-bank.json"));
    var interpreterSettings = configuration.GetSection("Interpreter").Get<LlmProviderSettings>() ?? new LlmProviderSettings();
    var interpreter = new OpenAiCompatibleLlmClient("Interpreter", interpreterSettings);
    var reader = new OperatorReader(interpreter, interpreterSettings.DefaultModel, script.Roads);
    var improviser = new Improviser(interpreter, interpreterSettings.DefaultModel, script);

    var scenarios = rehearsalIndex >= 0 ? ScenariosFrom(rehearsalIndex) : [];
    foreach (var scenario in scenarios)
    {
        Console.WriteLine($"{Environment.NewLine}===== rehearsal: {scenario.Name} (passable: {(scenario.PassableRoads.Count > 0 ? string.Join(", ", scenario.PassableRoads) : "none")})");
        var recorder = CallRecorder.CreateRun(Path.Combine(settings.CacheDirectory, "rehearsal"));
        var conversation = new Conversation(new SimulatedOperator(scenario, script.Roads), script, bank, studio, voiceSettings, reader, improviser, recorder, stepByStep);
        Report(await conversation.RunAsync());
    }

    if (callMode)
    {
        var client = new PhoneClient(settings.HubBaseUrl, RequireHubKey(), logPath);
        var recorder = CallRecorder.CreateRun(settings.CacheDirectory);
        Console.WriteLine($"Calling the operator for real at {DateTime.Now:HH:mm:ss}; transcript in {Path.GetFullPath(recorder.RunDirectory)}");
        var conversation = new Conversation(client, script, bank, studio, voiceSettings, reader, improviser, recorder, stepByStep);
        var outcome = await conversation.RunAsync();
        Report(outcome);
        Console.WriteLine($"{client.RequestsSent} request(s) to the hub.");
        Environment.ExitCode = outcome.Flag is null ? 1 : 0;
    }
}

IReadOnlyList<OperatorScenario> ScenariosFrom(int index)
{
    var name = index + 1 < args.Length && !args[index + 1].StartsWith("--") ? args[index + 1] : null;
    if (name is null)
        return OperatorScenario.All;

    return OperatorScenario.All.FirstOrDefault(scenario => scenario.Name == name) is { } match
        ? [match]
        : throw new ArgumentException($"Unknown scenario '{name}'; known: {string.Join(", ", OperatorScenario.All.Select(scenario => scenario.Name))}");
}

static void Report(CallOutcome outcome)
{
    Console.WriteLine($"{Environment.NewLine}Ending: {outcome.Ending}");
    Console.WriteLine($"Spoken turns: {outcome.TurnsSpoken}; {outcome.State.Describe()}");
    if (outcome.Flag is null && outcome.Hint is { Length: > 0 } hint)
        Console.WriteLine($"Last hint from the hub: {hint}");

    Console.WriteLine(outcome.Flag is null ? "No flag." : $"FLAG: {outcome.Flag}");
}

string RequireHubKey() =>
    string.IsNullOrWhiteSpace(apiKey) ? throw new InvalidOperationException("AI_DevsApiKey is missing; put it in appsettings.Development.json.") : apiKey;

static void PrintTake(BankEntry entry, Recording recording)
{
    Console.WriteLine($"[{(entry.Passed ? "OK" : "FAIL")}] {entry.Key,-26} take {entry.Take}  {entry.Bytes / 1024.0,5:F1} KB{(recording.FromCache ? " (cached)" : "")}  \"{entry.Text}\"");
    foreach (var check in entry.Checks)
    {
        var missing = check.MissingTerms.Count == 0 ? "" : $"   MISSING: {string.Join(", ", check.MissingTerms)}";
        Console.WriteLine($"       {check.Model,-24} \"{check.Transcript}\"{missing}");
    }
}

static void PrintUsage()
{
    Console.WriteLine("""
        Usage (S05E02 "phonecall"):
          --tests            offline tests: script wording, transcript check (no network, no key)
          --voices           synthesizes every script line and hears it back with each listener; writes
                             phonecall-cache/voice-bank.json (OpenAI audio only, no hub)
          --say "<text>"     synthesizes one text and prints what each listener hears (tuning pronunciation)
          --probe            sends only "start" and shows the reply: fields, operator audio and its transcript
                             (hub; nothing is said to the operator)
          --rehearsal [name] the whole call against a simulated operator (no hub). Without a name, every
                             scenario: plain, password-first, suspicious, reluctant, all-blocked
          --call             the real call. Add --step to confirm each line before it is sent.
        """);
}
