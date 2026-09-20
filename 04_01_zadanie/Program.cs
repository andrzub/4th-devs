using _04_01_zadanie;
using _04_01_zadanie.Agents;
using _04_01_zadanie.Hub;
using _04_01_zadanie.Llm;
using _04_01_zadanie.Mission;
using _04_01_zadanie.Oko;
using _04_01_zadanie.Tests;
using _04_01_zadanie.Tools;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("OkoEditor").Get<TaskSettings>() ?? new TaskSettings();
var apiKey = configuration["AI_DevsApiKey"] ?? string.Empty;

const string logPath = "okoeditor-log.jsonl";

var testsMode = args.Contains("--tests");
var guardIndex = Array.IndexOf(args, "--guard");
var helpApiMode = args.Contains("--help-api");
var panelMode = args.Contains("--panel");
var readIndex = Array.IndexOf(args, "--read");
var updateMode = args.Contains("--update");
var doneMode = args.Contains("--done");
var runMode = args.Contains("--run");
var submitEnabled = args.Contains("--submit");

if (!(testsMode || guardIndex >= 0 || helpApiMode || panelMode || readIndex >= 0 || updateMode || doneMode || runMode))
{
    PrintUsage();
    return;
}

// ---------------------------------------------------------------------------
// Offline modes. No network, no key: the rules that keep this run out of the
// console's write paths can be changed and re-checked in a second.
// ---------------------------------------------------------------------------

if (testsMode)
{
    Environment.ExitCode = OfflineTests.Run() ? 0 : 1;
    return;
}

if (guardIndex >= 0)
{
    foreach (var path in args.Skip(guardIndex + 1).TakeWhile(value => !value.StartsWith("--")))
    {
        var verdict = OkoPanelGuard.Evaluate(path);
        Console.WriteLine($"{(verdict.Allowed ? "allow " : "refuse")}  {path}");
        Console.WriteLine($"        {verdict.Reason}");
    }

    return;
}

if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Missing AI_DevsApiKey — set it in appsettings.Development.json.");

// ---------------------------------------------------------------------------
// Shared plumbing for every mode that touches the hub or the console.
// ---------------------------------------------------------------------------

var runDirectory = Path.Combine(settings.CacheDirectory, $"run-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");

using var panel = new OkoPanelClient(
    settings.PanelBaseUrl, settings.PanelLogin, settings.PanelPassword, apiKey,
    Path.Combine(settings.CacheDirectory, "panel"));

var editor = new OkoEditorClient(settings.HubBaseUrl, apiKey, logPath, settings.MaxHubRequests, settings.MinSecondsBetweenHubRequests);
var mission = new MissionState(settings);

if (helpApiMode)
{
    Console.WriteLine((await editor.HelpAsync()).Body);
    return;
}

if (panelMode)
{
    foreach (var page in OkoPages.All)
    {
        var records = await panel.ListAsync(page);
        mission.Remember(records);
        Console.WriteLine();
        Console.WriteLine($"=== {page} ({records.Count}) ===");
        foreach (var record in records)
            Console.WriteLine(record.Describe());
    }

    Console.WriteLine($"{Environment.NewLine}Console requests: {panel.RequestsSent}.");
    return;
}

if (readIndex >= 0)
{
    foreach (var reference in args.Skip(readIndex + 1).TakeWhile(value => !value.StartsWith("--")))
    {
        var parts = reference.Trim('/').Split('/');
        if (parts.Length != 2)
        {
            Console.WriteLine($"Expected '<page>/<id>', got '{reference}'.");
            continue;
        }

        Console.WriteLine();
        Console.WriteLine((await panel.ReadAsync(parts[0], parts[1])).Describe());
    }

    return;
}

// ---------------------------------------------------------------------------
// Manual editing, through the same guard the agent uses. --submit sends;
// without it the edit is judged and applied to the projection but not sent.
// ---------------------------------------------------------------------------

if (updateMode || doneMode)
{
    // The manual paths still need the projection populated, so an id is checked against a record
    // actually read rather than taken on faith.
    foreach (var page in OkoPages.Editable)
        mission.Remember(await panel.ListAsync(page));

    // No model here to register the classification table, so it is recovered in code from the
    // console's own coding note — the first note whose text parses into a well-formed table.
    foreach (var note in await panel.ListAsync(OkoPages.Notes))
    {
        var full = await panel.ReadAsync(note.Page, note.Id);
        if (mission.TryRegisterCodeBookFromNote(full.Content, out _))
        {
            Console.WriteLine($"Classification table recovered from {full.Page}/{full.Id}:");
            Console.WriteLine(mission.CodeBook!.Render());
            Console.WriteLine();
            break;
        }
    }

    if (updateMode)
    {
        var request = new UpdateRequest(
            ReadValue("--page") ?? string.Empty,
            ReadValue("--id") ?? string.Empty,
            ReadValue("--title"),
            ReadValue("--content"),
            ReadValue("--set-done"));

        var tool = new UpdateRecordTool(editor, mission, dryRun: !submitEnabled);
        Console.WriteLine(await tool.ExecuteAsync(System.Text.Json.JsonSerializer.Serialize(new
        {
            page = request.Page,
            id = request.Id,
            title = request.Title,
            content = request.Content,
            done = request.Done
        })));
    }

    if (doneMode)
        Console.WriteLine(await new FinishMissionTool(editor, mission, dryRun: !submitEnabled, maxRefusals: 0).ExecuteAsync("{}"));

    Console.WriteLine();
    Console.WriteLine(mission.RenderChecklist());
    return;
}

// ---------------------------------------------------------------------------
// The agent loop. Without --submit nothing reaches the okoeditor API: edits
// are judged and applied to a local projection of the console, a full
// rehearsal that leaves the real console untouched.
// ---------------------------------------------------------------------------

var agentSettings = configuration.GetSection("Agent").Get<LlmProviderSettings>()
    ?? throw new InvalidOperationException("Missing the 'Agent' configuration section.");

var operationRun = new Operation(panel, editor, mission, settings, submissionEnabled: submitEnabled);
var transcript = new Transcript(Path.Combine(runDirectory, "transcript.txt"));
var dryRun = !submitEnabled;

var agent = new AgentLoop(
    label: "editor",
    client: new OpenAiCompatibleLlmClient("Agent", agentSettings),
    systemPrompt: OkoPrompt.SystemPrompt,
    tools: [
        new ReadConsoleTool(panel, mission),
        new ReadRecordTool(panel, mission),
        new RegisterCodeBookTool(mission, Path.Combine(runDirectory, "codebook.json")),
        new UpdateRecordTool(editor, mission, dryRun),
        new FinishMissionTool(editor, mission, dryRun)
    ],
    isGoalReached: () => operationRun.IsSettled,
    transcript: transcript,
    maxIterations: settings.MaxIterations,
    hooks: new OkoHooks(operationRun));

Console.WriteLine($"Editing the OKO console{(submitEnabled ? "" : " (dry run, nothing is sent)")}. Transcript: {transcript.FilePath}");
Console.WriteLine();

var reconnaissance = await operationRun.BootstrapAsync();
Console.WriteLine(reconnaissance);
Console.WriteLine();

var result = await agent.RunAsync(OkoPrompt.BuildTask(reconnaissance, CentreOrders.Build(settings)));

Console.WriteLine();
Console.WriteLine($"Iterations: {result.Iterations}  tokens: {result.PromptTokens} in / {result.CompletionTokens} out  okoeditor calls: {editor.RequestsSent}");
if (result.Abort is not null)
    Console.WriteLine($"Run aborted: {result.Abort}");

Console.WriteLine();
Console.WriteLine(mission.RenderChecklist());
Console.WriteLine();
Console.WriteLine(mission.RenderHistory());

if (mission.FlagReceived)
    Console.WriteLine($"{Environment.NewLine}Flag: {mission.Flag}");
else if (!submitEnabled)
    Console.WriteLine($"{Environment.NewLine}Dry run — nothing reached the okoeditor API. Rerun with --run --submit to make the changes for real.");

return;

// ---------------------------------------------------------------------------

string? ReadValue(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static void PrintUsage()
{
    Console.WriteLine("""
        Usage:
          Offline (no network, no key):
            --tests                         checks of the path guard, the HTML parser, the classification table and the update guard
            --guard "<path>"...             ask the guard what it thinks of a console path, without requesting it

          Reading (console + API docs, no changes):
            --help-api                      the okoeditor API's own help
            --panel                         read every listing of the operator console
            --read <page>/<id>...           read one record in full

          Editing by hand (through the same guard the agent uses):
            --update --page <p> --id <id> [--title <t>] [--content <c>] [--set-done YES|NO] [--submit]
            --done [--submit]               run the verification
              (without --submit the edit is judged and shown, but nothing is sent)

          Agent loop:
            --run                           full loop, dry run — edits judged and applied to a local projection, nothing sent
            --run --submit                  full loop, edits and verification sent to the okoeditor API for real
        """);
}
