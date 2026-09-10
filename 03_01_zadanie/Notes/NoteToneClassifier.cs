using System.Diagnostics;
using System.Text.Json.Nodes;
using _03_01_zadanie.Llm;
using _03_01_zadanie.Observability;

namespace _03_01_zadanie.Notes;

/// <summary>
/// Asks the model the single question code cannot answer: does this operator statement claim
/// the equipment is fine, or that something is wrong? Everything around that question is an
/// optimisation — duplicates are collapsed before anything is sent, verdicts are cached on
/// disk between runs, statements travel in batches, and the reply is reduced to a list of the
/// few item numbers that are not routine.
/// </summary>
public sealed class NoteToneClassifier(
    ILlmClient llm,
    string model,
    ToneCache cache,
    RunLog log,
    UsageMeter meter,
    int batchSize,
    int maxParallelBatches)
{
    private const int MaxAttemptsPerBatch = 3;

    private static readonly string SystemPrompt = string.Join('\n',
        "You audit shift notes written by the operators of a power plant. Each numbered item is one statement about a sensor reading — either a whole note or a single clause of one.",
        "",
        "Classify every item by what it asserts about the equipment:",
        "- PROBLEM: something is wrong, suspicious, degraded or untrustworthy, or the reading needs follow-up, inspection, escalation or sign-off before it can be accepted.",
        "- UNCLEAR: the item asserts nothing about the condition of the equipment, or is genuinely ambiguous.",
        "- OK: everything else — the item asserts that the reading is healthy, normal, within tolerance, approved, or that no action is needed.",
        "",
        "Judge the assertion, not the vocabulary: \"nothing suggests a fault condition\" is OK even though it names a fault, and \"I closed this check without action\" is OK because it approves the reading.",
        "",
        "Reply with exactly two lines and nothing else:",
        "PROBLEM: <comma-separated item numbers>",
        "UNCLEAR: <comma-separated item numbers>",
        "",
        "Leave the value empty when no item belongs to a line. Every item you do not list counts as OK. Never restate an item, never explain, never output anything else.");

    /// <summary>Batch replies thrown away because they failed parsing or the control statements.</summary>
    public int RejectedReplies { get; private set; }

    public async Task<IReadOnlyDictionary<string, NoteTone>> ClassifyAsync(IReadOnlyList<string> statements, string trace, CancellationToken cancellationToken = default)
    {
        var distinct = statements.Distinct(StringComparer.Ordinal).ToList();
        var result = new Dictionary<string, NoteTone>(StringComparer.Ordinal);
        var pending = new List<string>();

        foreach (var statement in distinct)
        {
            if (cache.TryGet(statement, out var cached))
                result[statement] = cached;
            else
                pending.Add(statement);
        }

        log.Event(trace, "classification-planned", new JsonObject
        {
            ["requested"] = statements.Count,
            ["distinct"] = distinct.Count,
            ["cacheHits"] = result.Count,
            ["toClassify"] = pending.Count,
            ["batchSize"] = batchSize
        });

        Console.WriteLine($"[{trace}] {statements.Count:N0} statements -> {distinct.Count:N0} distinct, {result.Count:N0} already cached, {pending.Count:N0} to classify");

        if (pending.Count == 0)
            return result;

        var batches = Chunk(pending, batchSize).ToList();
        var gate = new SemaphoreSlim(Math.Max(1, maxParallelBatches));
        var verdicts = new Dictionary<string, NoteTone>[batches.Count];

        await Task.WhenAll(batches.Select(async (statementsInBatch, index) =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                verdicts[index] = await ClassifyBatchAsync(statementsInBatch, index, batches.Count, trace, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }));

        foreach (var batchVerdicts in verdicts)
        {
            foreach (var (statement, tone) in batchVerdicts)
            {
                result[statement] = tone;
                cache.Set(statement, tone);
            }
        }

        cache.Save();
        return result;
    }

    private async Task<Dictionary<string, NoteTone>> ClassifyBatchAsync(List<string> statements, int batchIndex, int batchCount, string trace, CancellationToken cancellationToken)
    {
        var batch = ClassificationBatch.Create(statements, batchIndex + 1);
        var userPrompt = batch.RenderPrompt();

        for (var attempt = 1; attempt <= MaxAttemptsPerBatch; attempt++)
        {
            var request = new LlmRequest
            {
                Model = model,
                Temperature = 0,
                Messages = [Message.System(SystemPrompt), Message.User(userPrompt)]
            };

            var stopwatch = Stopwatch.StartNew();
            var response = await llm.CompleteAsync(request, cancellationToken);
            stopwatch.Stop();

            var reply = ClassificationReply.Parse(response.Content, batch.Items.Count);
            var failure = batch.Verify(reply);

            var cost = meter.Record(trace, response.Usage, failure is null ? batch.StatementCount : 0);
            log.Generation(trace, model, batch.Items.Count, response.Usage.PromptTokens, response.Usage.CachedPromptTokens,
                response.Usage.CompletionTokens, cost, stopwatch.Elapsed,
                failure is null ? null : $"attempt {attempt} rejected: {failure}");

            log.SaveArtifact(Path.Combine(trace.Replace(':', '-'), $"batch-{batchIndex + 1:000}-attempt-{attempt}.txt"),
                $"=== system ==={Environment.NewLine}{SystemPrompt}{Environment.NewLine}{Environment.NewLine}" +
                $"=== user ==={Environment.NewLine}{userPrompt}{Environment.NewLine}" +
                $"=== raw reply ==={Environment.NewLine}{response.Content}{Environment.NewLine}{Environment.NewLine}" +
                $"=== outcome ==={Environment.NewLine}{failure ?? "accepted"}{Environment.NewLine}");

            if (failure is null)
            {
                Console.WriteLine($"  [{trace}] batch {batchIndex + 1}/{batchCount}: {batch.StatementCount} statements, {reply.Problem.Count} flagged, " +
                                  $"{response.Usage.PromptTokens} in / {response.Usage.CompletionTokens} out, ${cost:0.000000}, {stopwatch.Elapsed.TotalSeconds:0.0}s");
                return batch.CollectVerdicts(reply);
            }

            RejectedReplies++;
            Console.WriteLine($"  [{trace}] batch {batchIndex + 1}/{batchCount} attempt {attempt} rejected: {failure}");
        }

        throw new InvalidOperationException(
            $"Batch {batchIndex + 1} of trace '{trace}' failed {MaxAttemptsPerBatch} times. Verdicts from the batches that succeeded are cached, so re-running resumes from here.");
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
            yield return source.GetRange(i, Math.Min(size, source.Count - i));
    }
}
