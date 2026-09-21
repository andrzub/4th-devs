using System.Globalization;
using System.Text.Json.Nodes;
using _04_02_zadanie.Analysis;

namespace _04_02_zadanie.Mission;

/// <summary>
/// Matches the signatures coming out of the queue to the points they were ordered for. getResult
/// hands back one item at a time in whatever order the queue finished them, so a signature is only
/// usable if it can be tied back to its own date, hour and pitch - a code attached to the wrong
/// point is worse than a missing one, because it looks complete.
/// </summary>
public sealed class SignatureCollector(IReadOnlyList<ConfigPoint> points)
{
    private static readonly string[] SignatureFields = ["unlockCode", "signature", "hash", "md5"];

    private readonly Dictionary<string, string> _signatures = [];

    public IReadOnlyDictionary<string, string> Signatures => _signatures;

    public List<JsonObject> Unmatched { get; } = [];

    public bool Complete => points.All(point => _signatures.ContainsKey(point.Key));

    public IEnumerable<ConfigPoint> Missing => points.Where(point => !_signatures.ContainsKey(point.Key));

    /// <summary>Returns the point the signature belongs to, or null when the reply cannot be tied to one.</summary>
    public ConfigPoint? Accept(JsonObject reply)
    {
        var signature = SignatureFields
            .Select(field => reply[field]?.ToString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (signature is null)
        {
            Unmatched.Add(reply);
            return null;
        }

        var point = Identify(reply);

        if (point is null)
        {
            Unmatched.Add(reply);
            return null;
        }

        _signatures[point.Key] = signature;
        return point;
    }

    /// <summary>
    /// Last resort for a queue that hands back a bare signature: when exactly one point is still
    /// waiting and exactly one signature arrived without identification, the pairing is forced.
    /// Only safe because the run orders codes one at a time once this happens.
    /// </summary>
    public ConfigPoint? ForceSinglePending()
    {
        if (Unmatched.Count != 1)
            return null;

        var pending = Missing.ToList();
        if (pending.Count != 1)
            return null;

        var signature = SignatureFields
            .Select(field => Unmatched[0][field]?.ToString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (signature is null)
            return null;

        _signatures[pending[0].Key] = signature;
        Unmatched.Clear();
        return pending[0];
    }

    private ConfigPoint? Identify(JsonObject reply)
    {
        // The generator echoes what it signed under "signedParams", which is the only reason
        // several signatures can be ordered at once: without that echo a reply is just a hash.
        var signed = reply["signedParams"]?.AsObject() ?? reply;

        var date = signed["startDate"]?.ToString();
        var hour = signed["startHour"]?.ToString();

        if (date is not null && hour is not null)
        {
            var key = $"{date} {hour}";
            return points.FirstOrDefault(point => string.Equals(point.Key, key, StringComparison.Ordinal));
        }

        if (signed["timestamp"]?.ToString() is { } timestamp)
            return points.FirstOrDefault(point => string.Equals(point.Key, timestamp, StringComparison.Ordinal));

        // Pitch alone identifies a point only while no two points share it - true for a schedule
        // whose storms are all protected the same way and whose single production hour is not.
        if (signed["pitchAngle"]?.ToString() is { } pitch && double.TryParse(pitch, CultureInfo.InvariantCulture, out var angle))
        {
            var candidates = points.Where(point => Math.Abs(point.PitchAngle - angle) < 0.001 && !_signatures.ContainsKey(point.Key)).ToList();
            if (candidates.Count == 1)
                return candidates[0];
        }

        return null;
    }
}
