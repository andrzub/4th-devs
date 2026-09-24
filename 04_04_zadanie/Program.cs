using _04_04_zadanie;
using _04_04_zadanie.Agents;
using _04_04_zadanie.Filesystem;
using _04_04_zadanie.Hub;
using _04_04_zadanie.Llm;
using _04_04_zadanie.Mission;
using _04_04_zadanie.Notes;
using _04_04_zadanie.Tests;
using _04_04_zadanie.Tools;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("Filesystem").Get<TaskSettings>() ?? new TaskSettings();
var apiKey = configuration["AI_DevsApiKey"] ?? string.Empty;

const string logPath = "filesystem-log.jsonl";

var testsMode = args.Contains("--tests");
var notesMode = args.Contains("--notes");
var workspaceMode = args.Contains("--workspace");
var promptMode = args.Contains("--prompt");
var validateIndex = Array.IndexOf(args, "--validate");
var helpApiMode = args.Contains("--help-api");
var listIndex = Array.IndexOf(args, "--list");
var resetMode = args.Contains("--reset");
var runMode = args.Contains("--run");
var submitIndex = Array.IndexOf(args, "--submit");

if (!(testsMode || notesMode || workspaceMode || promptMode || validateIndex >= 0 || helpApiMode || listIndex >= 0 || resetMode || runMode || submitIndex >= 0))
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

var notes = new NoteLibrary(Path.Combine(AppContext.BaseDirectory, settings.NotesDirectory));
var workspace = new Workspace(Path.Combine(AppContext.BaseDirectory, settings.WorkspaceDirectory));

if (notesMode)
{
    foreach (var note in notes.ReadAll())
    {
        Console.WriteLine($"=== {note.Name} ({note.Content.Length} chars) ===");
        Console.WriteLine(note.Content);
        Console.WriteLine();
    }
}

if (workspaceMode)
{
    foreach (var document in workspace.ReadAll())
    {
        Console.WriteLine($"=== {document.Name} ({document.Content.Length} chars) ===");
        Console.WriteLine(document.Content);
        Console.WriteLine();
    }
}

if (promptMode)
{
    var preview = new FilingState(notes, workspace);
    Console.WriteLine("=== system prompt ===");
    Console.WriteLine(FilingPrompt.Build(workspace.Index.Content));
    Console.WriteLine();
    Console.WriteLine("=== task ===");
    Console.WriteLine(FilingPrompt.BuildTask(preview));
}

if (validateIndex >= 0)
{
    var planPath = ValueAfter(validateIndex) ?? throw new ArgumentException("--validate needs the path of a plan.json.");
    var filesystem = VirtualFilesystem.FromJson(File.ReadAllText(planPath));
    var report = PlanValidator.Validate(filesystem, ValidationSources.FromNotes(notes));

    Console.WriteLine(filesystem.Render());
    Console.WriteLine();
    Console.WriteLine(report.Render());
    Console.WriteLine();

    var batch = BatchBuilder.Build(filesystem, ApiVocabulary.Default);
    Console.WriteLine($"Batch preview: {batch.Count} actions ({filesystem.Directories.Count} directories, {filesystem.Files.Count} files).");
    Environment.ExitCode = report.IsValid ? 0 : 1;
}

if (!(helpApiMode || listIndex >= 0 || resetMode || runMode || submitIndex >= 0))
    return;

var runDirectory = Path.Combine(settings.CacheDirectory, $"run-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");

// ---------------------------------------------------------------------------
// The agent loop. Nothing here touches the hub: the agent files the notes into
// a local projection, the validator judges it, and the plan is saved for
// --submit. A model key is all it needs.
// ---------------------------------------------------------------------------

if (runMode)
{
    var agentSettings = configuration.GetSection("Agent").Get<LlmProviderSettings>()
        ?? throw new InvalidOperationException("Missing the 'Agent' configuration section.");

    var state = new FilingState(notes, workspace);
    var transcript = new Transcript(Path.Combine(runDirectory, "transcript.txt"));

    var agent = new AgentLoop(
        label: "archivist",
        client: new OpenAiCompatibleLlmClient("Agent", agentSettings),
        systemPrompt: FilingPrompt.Build(workspace.Index.Content),
        tools: [
            new ReadNoteTool(notes, state),
            new ReadTemplateTool(workspace, state),
            new ListFilesTool(state),
            new ReadFileTool(state),
            new WriteFileTool(state),
            new DeleteFileTool(state),
            new CheckPlanTool(state)
        ],
        isGoalReached: () => state.Accepted,
        transcript: transcript,
        maxIterations: settings.MaxIterations,
        hooks: new FilingHooks(state),
        maxNudges: 4);

    Console.WriteLine($"Filing Natan's notes into a local projection (nothing is sent). Transcript: {transcript.FilePath}");
    Console.WriteLine();

    var result = await agent.RunAsync(FilingPrompt.BuildTask(state));

    // The final validation is the run's own, whether or not the agent asked for one.
    var finalReport = state.Check();
    var planPath = Path.Combine(runDirectory, "plan.json");
    File.WriteAllText(planPath, state.Filesystem.ToJson());
    File.WriteAllText(Path.Combine(runDirectory, "validation.txt"), finalReport.Render());
    File.WriteAllText(Path.Combine(runDirectory, "batch.json"), BatchBuilder.Build(state.Filesystem, ApiVocabulary.Default).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

    Console.WriteLine();
    Console.WriteLine(state.Filesystem.Render());
    Console.WriteLine();
    Console.WriteLine(finalReport.Render());
    Console.WriteLine();
    Console.WriteLine($"Iterations: {result.Iterations}  tokens: {result.PromptTokens} in / {result.CompletionTokens} out  writes: {state.Writes} ({state.Replacements} replacements)  refusals: {state.Refusals}  deletes: {state.Deletes}");
    if (result.Abort is not null)
        Console.WriteLine($"Run aborted: {result.Abort}");

    Console.WriteLine();
    Console.WriteLine(finalReport.IsValid
        ? $"Plan saved to {planPath}. Send it with: --submit \"{planPath}\""
        : $"Plan saved to {planPath} but it is not valid; fix it (or rerun) before --submit.");
    return;
}

// ---------------------------------------------------------------------------
// Hub modes. Every call goes through the same client, is counted and logged.
// ---------------------------------------------------------------------------

if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Missing AI_DevsApiKey — set it in appsettings.Development.json.");

Directory.CreateDirectory(settings.CacheDirectory);
var hub = new FilesystemClient(settings.HubBaseUrl, apiKey, logPath, settings.MaxHubRequests, settings.MinSecondsBetweenHubRequests);

if (helpApiMode)
{
    var reply = await hub.HelpAsync();
    Console.WriteLine(reply.Describe());

    // Kept on disk so the API's own description can be reread without spending another call.
    var helpPath = Path.Combine(settings.CacheDirectory, "help.json");
    File.WriteAllText(helpPath, reply.Body);
    Console.WriteLine($"{Environment.NewLine}Saved to {helpPath}.");
}

if (listIndex >= 0)
{
    var path = ValueAfter(listIndex) ?? "/";
    Console.WriteLine($"=== listFiles {path} ===");
    Console.WriteLine((await hub.ListFilesAsync(path)).Describe());
}

if (resetMode)
{
    Console.WriteLine("=== reset ===");
    Console.WriteLine((await hub.ResetAsync()).Describe());
}

if (submitIndex >= 0)
{
    var planPath = ValueAfter(submitIndex) ?? throw new ArgumentException("--submit needs the path of a plan.json.");
    var filesystem = VirtualFilesystem.FromJson(File.ReadAllText(planPath));
    var report = PlanValidator.Validate(filesystem, ValidationSources.FromNotes(notes));

    Console.WriteLine(filesystem.Render());
    Console.WriteLine();
    Console.WriteLine(report.Render());
    Console.WriteLine();

    if (!report.IsValid)
    {
        Console.WriteLine("Refusing to submit a plan the validator rejects.");
        Environment.ExitCode = 1;
        return;
    }

    // Three requests, each with its own verdict: a clean slate, the whole structure, the verification.
    Console.WriteLine("=== reset ===");
    var resetReply = await hub.ResetAsync();
    Console.WriteLine(resetReply.Describe());
    if (!resetReply.IsSuccess)
    {
        Console.WriteLine("Reset failed; stopping before the batch.");
        Environment.ExitCode = 1;
        return;
    }

    var batch = BatchBuilder.Build(filesystem, ApiVocabulary.Default);
    Console.WriteLine();
    Console.WriteLine($"=== batch ({batch.Count} actions) ===");
    var batchReply = await hub.SendAsync(batch);
    Console.WriteLine(batchReply.Describe());
    if (!batchReply.IsSuccess)
    {
        Console.WriteLine("The batch was rejected; stopping before done. Check the filesystem with --list and the log.");
        Environment.ExitCode = 1;
        return;
    }

    Console.WriteLine();
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

static void PrintUsage()
{
    Console.WriteLine("""
        Usage:
          Offline (no network, no key):
            --tests                         checks of paths, the write guard, the ledger parser, the plan validator and the batch
            --notes                         print Natan's notes exactly as the agent will read them
            --workspace                     print the map of content and the templates the agent works from
            --prompt                        print the system prompt and the task the agent will receive
            --validate <plan.json>          judge a saved filesystem plan against the notes and preview its batch

          Agent (model key only, nothing is sent to the hub):
            --run                           file the notes into a local projection; saves plan.json, validation.txt and batch.json
                                            under filesystem-cache/run-<date>/

          Hub (each is a call to /verify):
            --help-api                      the filesystem API's own help, saved to filesystem-cache/help.json
            --list [path]                   listFiles on the hub's filesystem (read-only)
            --reset                         clear the hub's filesystem
            --submit <plan.json>            validate, then reset + batch + done; prints the flag when done accepts
        """);
}
