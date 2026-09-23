using System.Text.Json;
using System.Text.Json.Nodes;
using _04_03_zadanie.City;

namespace _04_03_zadanie.Mission;

/// <summary>
/// What the hub does not hand back on request and the operation must therefore write down itself:
/// how many scouts each transporter still carries (neither getObjects nor the preview backend says),
/// and which fields have been inspected with what result (getLogs drains its queue on every read, so
/// an entry is seen exactly once). Persisted between runs so the search resumes from the live board.
/// </summary>
public sealed class OperationLedger
{
    private const string PendingMessage = "(inspected; log entry not read yet)";

    private readonly string _path;
    private readonly Dictionary<string, int> _aboard = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LogEntry> _inspections = [];

    public OperationLedger(string path)
    {
        _path = path;
        if (!File.Exists(path))
            return;

        var root = JsonNode.Parse(File.ReadAllText(path));
        if (root?["aboard"] is JsonObject aboard)
            foreach (var (id, count) in aboard)
                if (count is not null)
                    _aboard[id] = count.GetValue<int>();

        if (root?["inspections"] is JsonArray inspections)
            _inspections.AddRange(OperationState.ParseLogs(inspections));
    }

    public IReadOnlyList<LogEntry> Inspections => _inspections;

    public IEnumerable<Coordinate> InspectedFields => _inspections.Where(entry => entry.Field is not null).Select(entry => entry.Field!.Value).Distinct();

    public int? AboardOf(string transporterId) => _aboard.TryGetValue(transporterId, out var count) ? count : null;

    public void RecordCreate(string transporterId, int aboard)
    {
        _aboard[transporterId] = aboard;
        Save();
    }

    public void RecordDismount(string transporterId, int dismounted)
    {
        _aboard[transporterId] = Math.Max(0, AboardOf(transporterId) ?? dismounted) - dismounted;
        Save();
    }

    /// <summary>An accepted inspect: the field counts as done even before its log entry has been read.</summary>
    public void RecordInspect(string scoutId, Coordinate field)
    {
        if (_inspections.Any(entry => entry.Field == field))
            return;

        _inspections.Add(new LogEntry(scoutId, field, PendingMessage));
        Save();
    }

    /// <summary>
    /// Entries read from getLogs. Whether the hub repeats old entries or drops them after a while, only
    /// the ones not seen before are returned; a placeholder left by RecordInspect gives way to the real wording.
    /// </summary>
    public IReadOnlyList<LogEntry> RecordLogs(IEnumerable<LogEntry> entries)
    {
        var fresh = new List<LogEntry>();
        foreach (var entry in entries)
        {
            var placeholder = _inspections.FindIndex(known => known.Field == entry.Field && known.Message == PendingMessage);
            if (placeholder >= 0)
            {
                _inspections[placeholder] = entry;
                fresh.Add(entry);
                continue;
            }

            if (_inspections.Contains(entry))
                continue;

            _inspections.Add(entry);
            fresh.Add(entry);
        }

        if (fresh.Count > 0)
            Save();

        return fresh;
    }

    /// <summary>Reads what an accepted reply says: crew from create, dismounted from dismount, the field from inspect, entries from getLogs.</summary>
    public void Record(JsonObject answer, JsonNode reply, OperationState state)
    {
        var action = answer["action"]?.GetValue<string>();
        var id = reply["object"]?.GetValue<string>() ?? answer["object"]?.GetValue<string>();

        switch (action)
        {
            case "create" when id is not null && answer["type"]?.GetValue<string>() == "transporter":
                RecordCreate(id, (reply["crew"] as JsonArray)?.Count ?? answer["passengers"]?.GetValue<int>() ?? 0);
                break;
            case "dismount" when id is not null:
                RecordDismount(id, (reply["dismounted"] as JsonArray)?.Count ?? answer["passengers"]?.GetValue<int>() ?? 0);
                break;
            case "inspect" when id is not null && state.FindUnit(id) is { } scout:
                RecordInspect(scout.Id, scout.Position);
                break;
            case "getLogs" when reply["logs"] is JsonArray logs:
                RecordLogs(OperationState.ParseLogs(logs));
                break;
        }
    }

    /// <summary>
    /// Brings the state up to date with what the ledger remembers: transporter loads and inspected
    /// fields. A single transporter of unknown load is inferred from the counters (scouts used minus
    /// scouts on the ground) and remembered; with several unknown ones nothing is guessed.
    /// </summary>
    public void Resolve(OperationState state)
    {
        state.FreshLogs.Clear();
        state.FreshLogs.AddRange(RecordLogs(state.Logs));
        state.Inspected.UnionWith(InspectedFields);

        var unknown = new List<Unit>();
        var knownAboard = 0;

        foreach (var transporter in state.Transporters.ToList())
        {
            if (AboardOf(transporter.Id) is { } aboard)
            {
                state.SetAboard(transporter.Id, aboard);
                knownAboard += aboard;
            }
            else
            {
                unknown.Add(transporter);
            }
        }

        if (unknown.Count != 1)
            return;

        var inferred = Math.Max(0, state.ScoutsUsed - state.Scouts.Count() - knownAboard);
        RecordCreate(unknown[0].Id, inferred);
        state.SetAboard(unknown[0].Id, inferred);
    }

    private void Save()
    {
        var aboard = new JsonObject();
        foreach (var (id, count) in _aboard)
            aboard[id] = count;

        var inspections = new JsonArray();
        foreach (var entry in _inspections)
            inspections.Add(new JsonObject { ["scout"] = entry.Scout, ["field"] = entry.Field?.ToString(), ["msg"] = entry.Message });

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var root = new JsonObject { ["aboard"] = aboard, ["inspections"] = inspections };
        File.WriteAllText(_path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
