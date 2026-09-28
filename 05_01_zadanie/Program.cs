using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using _05_01_zadanie;
using _05_01_zadanie.Analysis;
using _05_01_zadanie.Capture;
using _05_01_zadanie.Hub;
using _05_01_zadanie.Llm;
using _05_01_zadanie.Media;
using _05_01_zadanie.Routing;
using _05_01_zadanie.Tests;
using Microsoft.Extensions.Configuration;

Console.OutputEncoding = Encoding.UTF8;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("Radiomonitoring").Get<TaskSettings>() ?? new TaskSettings();
var apiKey = configuration["AI_DevsApiKey"] ?? string.Empty;

const string logPath = "radiomonitoring-log.jsonl";

var testsMode = args.Contains("--tests");
var collectMode = args.Contains("--collect");
var triageIndex = Array.IndexOf(args, "--triage");
var routeIndex = Array.IndexOf(args, "--route");
var mediaIndex = Array.IndexOf(args, "--media");
var analyzeIndex = Array.IndexOf(args, "--analyze");
var transmitIndex = Array.IndexOf(args, "--transmit");

if (!testsMode && !collectMode && triageIndex < 0 && routeIndex < 0 && mediaIndex < 0 && analyzeIndex < 0 && transmitIndex < 0)
{
    PrintUsage();
    return;
}

if (testsMode)
{
    Environment.ExitCode = OfflineTests.Run() ? 0 : 1;
    return;
}

if (collectMode)
{
    var client = new RadioClient(settings.HubBaseUrl, RequireHubKey(), logPath, settings.MinSecondsBetweenHubRequests);
    var store = CaptureStore.CreateRun(settings.CacheDirectory);
    Console.WriteLine($"Collecting into {Path.GetFullPath(store.RunDirectory)}");

    var start = await client.StartAsync();
    store.SaveStartReply(start.Body);
    Console.WriteLine($"start: {start.Describe()}");
    if (!start.IsSuccess)
        return;

    for (var number = 1; number <= settings.MaxListens; number++)
    {
        var reply = await client.ListenAsync();
        if (!reply.IsSuccess)
        {
            store.SaveFinalReply(reply.Body);
            Console.WriteLine($"listen #{number}: {reply.Describe()}");
            break;
        }

        var capture = store.Save(number, reply.Body);
        Console.WriteLine($"listen #{number:D3}: code {capture.Code} {capture.Kind}{Summary(capture)}");

        // Only a reply without material and outside the capture code ends the session; an empty code-100 reply is just silence.
        if (capture.Kind == CaptureKind.Empty && capture.Code != 100)
        {
            Console.WriteLine($"  hub says: {capture.Message}");
            break;
        }

        if (number == settings.MaxListens)
            Console.WriteLine($"Stopped at the MaxListens cap ({settings.MaxListens}) without an end-of-material reply.");
    }

    Console.WriteLine($"Done: {client.RequestsSent} requests. Next: --triage, --route");
}

if (triageIndex >= 0)
{
    var store = StoreFor(triageIndex);
    var report = Triage.Render(store.Load());
    File.WriteAllText(Path.Combine(store.RunDirectory, "triage.txt"), report);
    Console.WriteLine($"Run: {Path.GetFullPath(store.RunDirectory)}");
    Console.WriteLine(report);
}

if (routeIndex >= 0)
{
    // Offline: the routing table and the corpus as it stands before any media model is paid for.
    var store = StoreFor(routeIndex);
    var routed = RouteAll(store);
    var corpus = EvidenceCorpus.Build(routed, []);
    var table = RenderRouting(store, routed, corpus);
    File.WriteAllText(Path.Combine(store.RunDirectory, "routing.txt"), table);
    File.WriteAllText(Path.Combine(store.RunDirectory, "corpus-offline.txt"), corpus.Render());
    Console.WriteLine(table);
}

if (mediaIndex >= 0 || analyzeIndex >= 0)
{
    var store = StoreFor(Math.Max(mediaIndex, analyzeIndex));
    var routed = RouteAll(store);
    var readings = await ReadMediaAsync(store, routed);

    if (analyzeIndex >= 0)
    {
        var corpus = EvidenceCorpus.Build(routed, readings);
        var cities = CityTable.From(routed);
        File.WriteAllText(Path.Combine(store.RunDirectory, "corpus.txt"), corpus.Render());
        Console.WriteLine($"Corpus: {corpus.SectionCount} sections, {corpus.Render().Length:N0} chars; city records: {string.Join(", ", cities.Records.Select(r => $"{r.Name} ({r.Area})"))}");

        var analystSettings = configuration.GetSection("Analyst").Get<LlmProviderSettings>() ?? new LlmProviderSettings();
        var analyst = new Analyst(new OpenAiCompatibleLlmClient("Analyst", analystSettings), analystSettings.DefaultModel);
        var (findings, response) = await analyst.AnalyzeAsync(corpus, cities);
        Console.WriteLine($"[analyst] {analystSettings.DefaultModel}: {response.Usage.PromptTokens:N0} in / {response.Usage.CompletionTokens:N0} out");
        File.WriteAllText(Path.Combine(store.RunDirectory, "analyst-findings.json"), response.Content);

        var result = ReportValidator.Validate(findings, corpus, cities);
        Console.WriteLine();
        Console.WriteLine($"City:        {findings.CityName}  (evidence {string.Join(", ", findings.CityEvidence)})");
        Console.WriteLine($"Reasoning:   {findings.CityReasoning}");
        Console.WriteLine($"Warehouses:  {findings.WarehousesCount}  \"{findings.WarehousesQuote}\"");
        Console.WriteLine($"Phone:       {findings.PhoneNumber}  \"{findings.PhoneQuote}\"");
        Console.WriteLine($"Doubts:      {findings.Doubts}");
        foreach (var warning in result.Warnings)
            Console.WriteLine($"WARNING: {warning}");
        foreach (var error in result.Errors)
            Console.WriteLine($"ERROR: {error}");

        var answerPath = Path.Combine(store.RunDirectory, "answer.json");
        if (result.IsValid)
        {
            File.WriteAllText(answerPath, result.Report!.ToAnswer().ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            Console.WriteLine($"{Environment.NewLine}Answer written to {Path.GetFullPath(answerPath)} — nothing sent. Send it with --transmit.");
        }
        else
        {
            File.Delete(answerPath);
            Console.WriteLine($"{Environment.NewLine}No answer written: the findings did not pass validation.");
        }
    }
}

if (transmitIndex >= 0)
{
    var store = StoreFor(transmitIndex);
    var answerPath = Path.Combine(store.RunDirectory, "answer.json");
    if (!File.Exists(answerPath))
        throw new FileNotFoundException($"No validated answer at {answerPath}; run --analyze first.");

    var answer = JsonNode.Parse(File.ReadAllText(answerPath))!.AsObject();
    Console.WriteLine($"Transmitting {answer.ToJsonString()}");
    var client = new RadioClient(settings.HubBaseUrl, RequireHubKey(), logPath, settings.MinSecondsBetweenHubRequests);
    var reply = await client.SendAsync(answer);
    File.WriteAllText(Path.Combine(store.RunDirectory, "transmit-reply.json"), reply.Body);
    Console.WriteLine(reply.Describe());
}

List<RoutedCapture> RouteAll(CaptureStore store) =>
    store.Load().Select(capture => Router.Decide(capture, settings.MaxInlineDocumentBytes)).ToList();

async Task<List<MediaReading>> ReadMediaAsync(CaptureStore store, List<RoutedCapture> routed)
{
    var media = routed.Where(item => item.Route is Route.Audio or Route.Image).ToList();
    if (media.Count == 0)
        return [];

    var mediaSettings = configuration.GetSection("Media").Get<LlmProviderSettings>() ?? new LlmProviderSettings();
    var interpreter = new MediaInterpreter(new OpenAiCompatibleLlmClient("Media", mediaSettings), mediaSettings.DefaultModel,
        Path.Combine(settings.CacheDirectory, "media-cache"), settings.ImageMaxEdgePixels);

    var readings = new List<MediaReading>();
    foreach (var item in media)
    {
        var reading = await interpreter.ReadAsync(item);
        readings.Add(reading);
        File.WriteAllText(Path.Combine(store.RunDirectory, $"media-{item.Capture.Number:D3}.txt"), reading.Text);
        var usage = reading.FromCache ? "cached" : $"{reading.PromptTokens:N0} in / {reading.CompletionTokens:N0} out";
        Console.WriteLine($"[media] #{item.Capture.Number:D3} {item.Route} via {reading.Model}: {usage}");
        Console.WriteLine($"        {Preview(reading.Text, 300)}");
    }

    return readings;
}

string RenderRouting(CaptureStore store, List<RoutedCapture> routed, EvidenceCorpus corpus)
{
    var output = new StringBuilder();
    output.AppendLine($"Run: {Path.GetFullPath(store.RunDirectory)}");
    output.AppendLine();
    foreach (var item in routed)
        output.AppendLine($"#{item.Capture.Number:D3}  {item.Route,-8}  {item.Reason}{(item.Route == Route.Morse ? $"  -> \"{item.LocalText}\"" : "")}");

    output.AppendLine();
    output.AppendLine("Routes: " + string.Join(", ", routed.GroupBy(item => item.Route).Select(g => $"{g.Key} {g.Count()}")));

    // A rough 4-characters-per-token estimate: enough to compare orders of magnitude, not to bill.
    var rawChars = Directory.GetFiles(store.RunDirectory, "capture-???.json").Sum(path => new FileInfo(path).Length);
    var corpusChars = corpus.Render().Length;
    output.AppendLine($"Everything verbatim to one model: ~{rawChars / 4:N0} tokens ({rawChars:N0} chars of replies, Base64 included)");
    output.AppendLine($"Routed text corpus for the analyst: ~{corpusChars / 4:N0} tokens ({corpusChars:N0} chars), plus {routed.Count(i => i.Route is Route.Audio or Route.Image)} media calls");
    return output.ToString();
}

CaptureStore StoreFor(int modeIndex)
{
    var path = modeIndex + 1 < args.Length && !args[modeIndex + 1].StartsWith("--") ? args[modeIndex + 1] : null;
    return path is not null ? new CaptureStore(path) : CaptureStore.Latest(settings.CacheDirectory);
}

string RequireHubKey() =>
    string.IsNullOrWhiteSpace(apiKey) ? throw new InvalidOperationException("AI_DevsApiKey is missing; put it in appsettings.Development.json.") : apiKey;

static string Preview(string text, int length)
{
    var flat = text.ReplaceLineEndings(" ");
    return flat.Length <= length ? flat : flat[..length] + "…";
}

static string Summary(Capture capture) => capture.Kind switch
{
    CaptureKind.Transcription => $" ({capture.Transcription!.Length} ch)",
    CaptureKind.Attachment => $" {capture.Meta} {capture.Content!.Length:N0} B",
    _ => $" \"{capture.Message}\""
};

static void PrintUsage()
{
    Console.WriteLine("""
        Usage (S05E01 "radiomonitoring"). [run dir] defaults to the latest radiomonitoring-cache/run-*.
          --tests                offline tests: router, noise filter, Morse, area rounding, report validation
          --collect              start + listen until the hub ends the material (hub, no model)
          --triage [run dir]     census of a run: kinds, codes, file types (offline)
          --route [run dir]      routing decision per capture + corpus size estimate (offline, no model)
          --media [run dir]      audio and images through the media model, cached by content (no hub)
          --analyze [run dir]    media + one analyst call + validation -> answer.json (nothing sent)
          --transmit [run dir]   sends answer.json with action "transmit" (the only call that submits)
        """);
}
