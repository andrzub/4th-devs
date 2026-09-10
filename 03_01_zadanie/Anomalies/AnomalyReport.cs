using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using _03_01_zadanie.Notes;
using _03_01_zadanie.Sensors;

namespace _03_01_zadanie.Anomalies;

/// <summary>
/// Combines the deterministic data checks with the model's reading of the operator notes into
/// the list of file identifiers the hub expects, and renders the reasoning behind it.
/// </summary>
public sealed class AnomalyReport
{
    private AnomalyReport(IReadOnlyList<FileVerdict> verdicts)
    {
        Verdicts = verdicts;
        Anomalies = verdicts.Where(verdict => verdict.IsAnomaly).ToList();
        NeedsReview = verdicts.Where(verdict => verdict.NeedsHumanReview).ToList();
    }

    public IReadOnlyList<FileVerdict> Verdicts { get; }
    public IReadOnlyList<FileVerdict> Anomalies { get; }
    public IReadOnlyList<FileVerdict> NeedsReview { get; }

    public IReadOnlyList<string> AnomalyIds => Anomalies.Select(verdict => verdict.Id).ToList();

    public static AnomalyReport Build(IReadOnlyList<SensorReading> readings, IReadOnlyDictionary<string, NoteTone> tones)
    {
        var verdicts = readings.Select(reading => new FileVerdict
        {
            Reading = reading,
            DataFaults = ReadingValidator.Validate(reading),
            Tone = tones.TryGetValue(reading.OperatorNotes, out var tone) ? tone : NoteTone.Unclear
        }).ToList();

        return new AnomalyReport(verdicts);
    }

    public string Render()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Files analysed:       {Verdicts.Count:N0}");
        sb.AppendLine($"Anomalies found:      {Anomalies.Count:N0}");
        sb.AppendLine();
        sb.AppendLine("By category:");

        foreach (var group in Verdicts.GroupBy(verdict => verdict.Category).OrderByDescending(group => group.Count()))
            sb.AppendLine($"  {group.Key,-42} {group.Count(),6:N0}");

        sb.AppendLine();
        sb.AppendLine("Anomalies in detail:");
        foreach (var verdict in Anomalies)
            sb.AppendLine($"  {verdict.Id}  [{verdict.Reading.SensorType}]  {string.Join("; ", verdict.Reasons)}");

        if (NeedsReview.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Not submitted, but worth a human look ({NeedsReview.Count}) — healthy data with a note that claims nothing either way:");
            foreach (var verdict in NeedsReview)
                sb.AppendLine($"  {verdict.Id}  \"{verdict.Reading.OperatorNotes}\"");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Writes the payload exactly as the hub expects it, so submitting is a separate, reviewable
    /// step rather than something that happens as a side effect of the analysis.
    /// </summary>
    public void WriteAnswerFile(string path, string apiKey)
    {
        var ids = new JsonArray();
        foreach (var id in AnomalyIds)
            ids.Add(id);

        var payload = new JsonObject
        {
            ["apikey"] = apiKey.Length > 0 ? apiKey : "PUT-YOUR-KEY-HERE",
            ["task"] = "evaluation",
            ["answer"] = new JsonObject { ["recheck"] = ids }
        };

        File.WriteAllText(path, payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static IReadOnlyList<string> ReadAnswerFile(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("answer").GetProperty("recheck")
            .EnumerateArray()
            .Select(element => element.ValueKind == JsonValueKind.Number ? element.GetInt32().ToString("0000") : element.GetString() ?? "")
            .Where(id => id.Length > 0)
            .ToList();
    }
}
