using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using _03_01_zadanie.Notes;
using _03_01_zadanie.Observability;

namespace _03_01_zadanie.Evals;

public sealed class EvalCase
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NoteTone Expected { get; set; }
}

public sealed class EvalDataset
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<EvalCase> Cases { get; set; } = new();
}

public sealed record EvalOutcome(EvalCase Case, NoteTone Actual)
{
    public bool Passed => Actual == Case.Expected;
}

/// <summary>
/// The offline evaluation of the note classifier: a small hand-labelled dataset run through
/// exactly the same path as production data, scored per class. It exists because the classifier
/// cannot be checked against the sensor archive itself — nothing in those files says what an
/// operator meant. Its score gates the answer: a classifier that fails here would silently
/// change which files end up in the submission.
/// </summary>
public sealed class NoteToneEval(NoteToneResolver resolver, RunLog log)
{
    public static EvalDataset Load(string path)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<EvalDataset>(File.ReadAllText(path), options)
               ?? throw new InvalidOperationException($"Could not read the eval dataset at {path}.");
    }

    public async Task<EvalResult> RunAsync(EvalDataset dataset, ClassificationTier tier, CancellationToken cancellationToken = default)
    {
        var notes = dataset.Cases.Select(evalCase => evalCase.Note).ToList();
        var tones = await resolver.ResolveAsync(notes, tier, cancellationToken);

        var outcomes = dataset.Cases
            .Select(evalCase => new EvalOutcome(evalCase, tones[evalCase.Note]))
            .ToList();

        var result = new EvalResult(dataset.Name, tier, outcomes);

        log.Event($"eval:{tier.ToString().ToLowerInvariant()}", "eval-completed", new JsonObject
        {
            ["dataset"] = dataset.Name,
            ["cases"] = outcomes.Count,
            ["passed"] = outcomes.Count(outcome => outcome.Passed),
            ["accuracy"] = Math.Round(result.Accuracy, 4)
        });

        return result;
    }
}

public sealed class EvalResult(string datasetName, ClassificationTier tier, IReadOnlyList<EvalOutcome> outcomes)
{
    private static readonly NoteTone[] Tones = Enum.GetValues<NoteTone>();

    public IReadOnlyList<EvalOutcome> Outcomes => outcomes;
    public IReadOnlyList<EvalOutcome> Failures => outcomes.Where(outcome => !outcome.Passed).ToList();
    public double Accuracy => outcomes.Count == 0 ? 0 : (double)outcomes.Count(outcome => outcome.Passed) / outcomes.Count;

    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Dataset '{datasetName}' at the {tier.ToString().ToLowerInvariant()} tier: {outcomes.Count(outcome => outcome.Passed)}/{outcomes.Count} correct ({Accuracy:P1})");
        sb.AppendLine();

        sb.AppendLine("Confusion matrix (rows = expected, columns = classified):");
        sb.AppendLine($"  {"",-10}{string.Join("", Tones.Select(tone => $"{tone,10}"))}");
        foreach (var expected in Tones)
        {
            var row = Tones.Select(actual => outcomes.Count(outcome => outcome.Case.Expected == expected && outcome.Actual == actual));
            sb.AppendLine($"  {expected,-10}{string.Join("", row.Select(count => $"{count,10}"))}");
        }

        sb.AppendLine();
        sb.AppendLine($"  {"class",-10}{"support",9}{"precision",11}{"recall",9}{"F1",8}");
        foreach (var tone in Tones)
        {
            var truePositives = outcomes.Count(outcome => outcome.Case.Expected == tone && outcome.Actual == tone);
            var classified = outcomes.Count(outcome => outcome.Actual == tone);
            var support = outcomes.Count(outcome => outcome.Case.Expected == tone);

            var precision = classified == 0 ? 0 : (double)truePositives / classified;
            var recall = support == 0 ? 0 : (double)truePositives / support;
            var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);

            sb.AppendLine($"  {tone,-10}{support,9}{precision,11:P1}{recall,9:P1}{f1,8:0.00}");
        }

        if (Failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Failures:");
            foreach (var failure in Failures)
                sb.AppendLine($"  {failure.Case.Id} ({failure.Case.Source}): expected {failure.Case.Expected}, got {failure.Actual}{Environment.NewLine}    \"{failure.Case.Note}\"");
        }

        return sb.ToString();
    }
}
