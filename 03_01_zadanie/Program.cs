using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using _03_01_zadanie;
using _03_01_zadanie.Anomalies;
using _03_01_zadanie.Evals;
using _03_01_zadanie.Hub;
using _03_01_zadanie.Llm;
using _03_01_zadanie.Notes;
using _03_01_zadanie.Observability;
using _03_01_zadanie.Sensors;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var apiKey = configuration["AI_DevsApiKey"] ?? string.Empty;
var classifierSettings = configuration.GetSection("Classifier").Get<LlmProviderSettings>() ?? new LlmProviderSettings();
var settings = configuration.GetSection("Sensors").Get<TaskSettings>() ?? new TaskSettings();

var mode = args.FirstOrDefault(arg => arg.StartsWith("--")) ?? "--help";
var refresh = args.Contains("--refresh");
var skipEvals = args.Contains("--skip-evals");
var tier = ReadTier(args);

var archive = new SensorArchive(settings.ArchiveUrl, settings.CacheDirectory);
var answerPath = "answer.json";
var hubLogPath = "evaluation-log.jsonl";

try
{
    switch (mode)
    {
        case "--fetch":
            await FetchAsync();
            break;
        case "--analyze":
            await AnalyzeAsync();
            break;
        case "--classify":
            await ClassifyAsync();
            break;
        case "--selftest":
            return PipelineSelfTest.Run();
        case "--evals":
            return await RunEvalsAsync();
        case "--report":
            return await ReportAsync();
        case "--submit":
            return await SubmitAsync();
        default:
            PrintHelp();
            break;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

return 0;

// ---------------------------------------------------------------------------
// Modes
// ---------------------------------------------------------------------------

async Task FetchAsync()
{
    var count = await archive.EnsureDownloadedAsync(refresh);
    Console.WriteLine($"{count:N0} sensor files ready in {archive.ExtractedPath}");
}

/// <summary>
/// The whole deterministic half of the task, plus the numbers needed to choose how to spend
/// money on the other half. Touches no API and costs nothing.
/// </summary>
async Task AnalyzeAsync()
{
    await archive.EnsureDownloadedAsync(refresh);
    var readings = archive.LoadAll();
    var faultsById = readings.ToDictionary(reading => reading.Id, ReadingValidator.Validate);
    var faulty = readings.Where(reading => faultsById[reading.Id].Count > 0).ToList();

    Console.WriteLine($"Files:                {readings.Count:N0}");
    Console.WriteLine($"Distinct sensor_type: {readings.Select(reading => reading.SensorType).Distinct().Count()}");
    Console.WriteLine($"Files with bad data:  {faulty.Count:N0}   (settled here, no model involved)");
    Console.WriteLine();

    Console.WriteLine("Faults by kind and channel:");
    foreach (var group in faulty
                 .SelectMany(reading => faultsById[reading.Id])
                 .GroupBy(fault => (fault.Kind, fault.Channel))
                 .OrderByDescending(group => group.Count()))
    {
        Console.WriteLine($"  {group.Key.Kind,-28} {group.Key.Channel,-12} {group.Count(),5}");
    }

    Console.WriteLine();
    Console.WriteLine("Faulty files:");
    foreach (var reading in faulty)
        Console.WriteLine($"  {reading.Id}  [{reading.SensorType}]  {string.Join("; ", faultsById[reading.Id].Select(fault => fault.Describe()))}");

    var notes = readings.Select(reading => reading.OperatorNotes).ToList();
    var (distinctNotes, distinctStatements) = NoteToneResolver.CountWork(notes);
    var decomposable = notes.Distinct(StringComparer.Ordinal).Count(NoteDecomposer.Decomposes);

    Console.WriteLine();
    Console.WriteLine("Operator notes — what is left for the model:");
    Console.WriteLine($"  notes in the archive:            {notes.Count,7:N0}");
    Console.WriteLine($"  distinct notes:                  {distinctNotes,7:N0}   ({(double)distinctNotes / notes.Count:P1} of the archive)");
    Console.WriteLine($"  of those, built from clauses:    {decomposable,7:N0}");
    Console.WriteLine($"  distinct clauses across them:    {distinctStatements,7:N0}");
    Console.WriteLine();
    Console.WriteLine($"  {"tier",-10}{"statements",12}{"est. input tok",16}{"est. USD",12}");
    Console.WriteLine($"  {"notes",-10}{distinctNotes,12:N0}{EstimateTokens(notes.Distinct(StringComparer.Ordinal), distinctNotes, settings.BatchSize),16:N0}{EstimateCost(notes.Distinct(StringComparer.Ordinal), distinctNotes),12:0.0000}");

    var statements = notes.Distinct(StringComparer.Ordinal).SelectMany(NoteDecomposer.Split).Distinct(StringComparer.Ordinal).ToList();
    Console.WriteLine($"  {"clauses",-10}{distinctStatements,12:N0}{EstimateTokens(statements, distinctStatements, settings.BatchSize),16:N0}{EstimateCost(statements, distinctStatements),12:0.0000}");
    Console.WriteLine();
    Console.WriteLine("  (token counts are a ~4 chars/token approximation, not a tokenizer)");
}

async Task ClassifyAsync()
{
    await archive.EnsureDownloadedAsync(refresh);
    var readings = archive.LoadAll();

    var (log, meter, resolver) = BuildClassifier();
    var notes = readings.Select(reading => reading.OperatorNotes).ToList();
    var tones = await resolver.ResolveAsync(notes, tier);

    Console.WriteLine();
    Console.WriteLine("Notes by verdict (distinct notes / files):");
    foreach (var group in tones.GroupBy(pair => pair.Value).OrderBy(group => group.Key))
    {
        var files = readings.Count(reading => group.Any(pair => pair.Key == reading.OperatorNotes));
        Console.WriteLine($"  {group.Key,-10} {group.Count(),6:N0} {files,8:N0}");
    }

    PrintModelUsage(meter, log);
}

async Task<int> RunEvalsAsync()
{
    var (log, meter, resolver) = BuildClassifier();
    var result = await RunEvalAsync(resolver, log);

    Console.WriteLine();
    Console.WriteLine(result.Render());
    PrintModelUsage(meter, log);

    if (result.Accuracy + 1e-9 < settings.MinAcceptableEvalAccuracy)
    {
        Console.Error.WriteLine($"Eval below the {settings.MinAcceptableEvalAccuracy:P0} bar required to trust the classifier.");
        return 1;
    }

    return 0;
}

/// <summary>
/// Builds the answer, but only after the classifier has proven itself on the labelled dataset.
/// The gate is the point of the exercise: the model's reading of the notes decides which files
/// are submitted, so a regression there is invisible in the output but fatal to the result.
/// </summary>
async Task<int> ReportAsync()
{
    await archive.EnsureDownloadedAsync(refresh);
    var readings = archive.LoadAll();

    var (log, meter, resolver) = BuildClassifier();

    if (!skipEvals)
    {
        var evalResult = await RunEvalAsync(resolver, log);
        Console.WriteLine(evalResult.Render());

        if (evalResult.Accuracy + 1e-9 < settings.MinAcceptableEvalAccuracy)
        {
            Console.Error.WriteLine($"Refusing to build an answer: the classifier scored {evalResult.Accuracy:P1}, below the required {settings.MinAcceptableEvalAccuracy:P0}.");
            Console.Error.WriteLine("Fix the prompt or the tier and re-run, or pass --skip-evals to build the answer anyway.");
            return 1;
        }
    }

    var tones = await resolver.ResolveAsync(readings.Select(reading => reading.OperatorNotes).ToList(), tier);
    var report = AnomalyReport.Build(readings, tones);

    Console.WriteLine();
    Console.WriteLine(report.Render());

    report.WriteAnswerFile(answerPath, apiKey);
    log.Event("report", "answer-built", new JsonObject { ["anomalies"] = report.Anomalies.Count, ["path"] = answerPath });

    Console.WriteLine($"Answer written to {answerPath}");
    Console.WriteLine($"recheck = [{string.Join(", ", report.AnomalyIds.Select(id => $"\"{id}\""))}]");
    PrintModelUsage(meter, log);
    Console.WriteLine("Nothing has been sent yet — review the list above, then run with --submit.");

    return 0;
}

async Task<int> SubmitAsync()
{
    if (apiKey.Length == 0)
        throw new InvalidOperationException("Missing AI_DevsApiKey — set it in appsettings.Development.json.");

    var fileArgIndex = Array.IndexOf(args, "--file");
    var path = fileArgIndex >= 0 && fileArgIndex + 1 < args.Length ? args[fileArgIndex + 1] : answerPath;

    if (!File.Exists(path))
        throw new InvalidOperationException($"No answer file at {path} — run with --report first.");

    var ids = AnomalyReport.ReadAnswerFile(path);
    Console.WriteLine($"Submitting {ids.Count} identifiers from {path} ...");

    var hub = new HubClient(settings.HubBaseUrl, apiKey, hubLogPath);
    var submission = await hub.SubmitRecheckAsync(ids);

    Console.WriteLine(submission.Render());
    return submission.IsSuccess ? 0 : 1;
}

// ---------------------------------------------------------------------------
// Wiring and shared helpers
// ---------------------------------------------------------------------------

(RunLog Log, UsageMeter Meter, NoteToneResolver Resolver) BuildClassifier()
{
    var log = new RunLog(settings.CacheDirectory, "classification-log.jsonl");
    var meter = new UsageMeter(classifierSettings);
    var llm = new OpenAiCompatibleLlmClient("Classifier", classifierSettings);
    var cache = new ToneCache(settings.CacheDirectory, classifierSettings.DefaultModel);

    log.Event($"classify:{tier.ToString().ToLowerInvariant()}", "run-started", new JsonObject
    {
        ["model"] = classifierSettings.DefaultModel,
        ["tier"] = tier.ToString(),
        ["batchSize"] = settings.BatchSize,
        ["cachedVerdicts"] = cache.Count
    });

    var classifier = new NoteToneClassifier(llm, classifierSettings.DefaultModel, cache, log, meter, settings.BatchSize, settings.MaxParallelBatches);
    return (log, meter, new NoteToneResolver(classifier));
}

async Task<EvalResult> RunEvalAsync(NoteToneResolver resolver, RunLog log)
{
    var datasetPath = Path.Combine(AppContext.BaseDirectory, "Evals", "note-tone.labeled.json");
    var dataset = NoteToneEval.Load(datasetPath);

    Console.WriteLine($"Running eval '{dataset.Name}' ({dataset.Cases.Count} labelled cases) at the {tier.ToString().ToLowerInvariant()} tier...");
    return await new NoteToneEval(resolver, log).RunAsync(dataset, tier);
}

static ClassificationTier ReadTier(string[] args)
{
    var index = Array.IndexOf(args, "--tier");
    if (index < 0 || index + 1 >= args.Length)
        return ClassificationTier.Clauses;

    return Enum.TryParse<ClassificationTier>(args[index + 1], ignoreCase: true, out var parsed)
        ? parsed
        : throw new ArgumentException($"Unknown tier '{args[index + 1]}'. Use 'clauses' or 'notes'.");
}

/// <summary>Rough token count for a batched classification pass, prompt overhead included.</summary>
static int EstimateTokens(IEnumerable<string> statements, int count, int batchSize)
{
    const int SystemPromptTokens = 260;
    var batches = Math.Max(1, (int)Math.Ceiling((double)count / batchSize));
    return batches * SystemPromptTokens + statements.Sum(statement => statement.Length / 4 + 4);
}

decimal EstimateCost(IEnumerable<string> statements, int count)
{
    var inputTokens = EstimateTokens(statements, count, settings.BatchSize);
    var batches = Math.Max(1, (int)Math.Ceiling((double)count / settings.BatchSize));

    return inputTokens * classifierSettings.InputPricePerMillionTokens / 1_000_000m
         + batches * 30 * classifierSettings.OutputPricePerMillionTokens / 1_000_000m;
}

void PrintModelUsage(UsageMeter meter, RunLog log)
{
    Console.WriteLine();
    Console.WriteLine("Model usage this run:");
    Console.Write(meter.RenderTable());
    Console.WriteLine($"  transcripts: {log.RunDirectory}");
}

void PrintHelp()
{
    Console.WriteLine("""
        S03E01 "evaluation" — finding anomalies in 10k sensor readings.

        Modes:
          --fetch                     download and unpack the sensor archive
          --analyze                   deterministic pass only: faulty files, note statistics,
                                      and what each classification tier would cost. No API calls.
          --selftest                  offline checks of everything around the classifier:
                                      reply parsing, control statements, anomaly rules. No API calls.
          --classify [--tier T]       classify the operator notes and print the verdict spread
          --evals [--tier T]          run the labelled dataset and print precision/recall
          --report [--tier T]         evals, then classification, then the full anomaly list
                                      written to answer.json. Sends nothing.
          --submit [--file PATH]      post answer.json to the hub (the only outbound call)

        Options:
          --tier clauses|notes        granularity of note classification (default: clauses)
          --refresh                   re-download the archive instead of using the cache
          --skip-evals                let --report build an answer without the quality gate
        """);
}
