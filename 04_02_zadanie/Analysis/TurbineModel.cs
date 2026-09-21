using System.Globalization;
using System.Text.Json.Nodes;

namespace _04_02_zadanie.Analysis;

public sealed record YieldAnchor(double WindMs, ValueRange Percent);

/// <summary>
/// The turbine's power curve, read from the documentation the API serves rather than written down
/// here. The documentation is the one report available outside the service window, so nothing about
/// the machine has to be guessed in advance - and a curve that changes stays a data change.
/// </summary>
public sealed class TurbineModel
{
    private readonly IReadOnlyList<YieldAnchor> _windCurve;
    private readonly IReadOnlyDictionary<int, double> _pitchFactors;

    private TurbineModel(double ratedPowerKw, double cutoffWindMs, double minOperationalWindMs, IReadOnlyList<YieldAnchor> windCurve, IReadOnlyDictionary<int, double> pitchFactors)
    {
        RatedPowerKw = ratedPowerKw;
        CutoffWindMs = cutoffWindMs;
        MinOperationalWindMs = minOperationalWindMs;
        _windCurve = windCurve;
        _pitchFactors = pitchFactors;
    }

    public double RatedPowerKw { get; }

    /// <summary>Wind above this speed breaks the blades; the documentation calls everything past it "damage".</summary>
    public double CutoffWindMs { get; }

    public double MinOperationalWindMs { get; }

    public IReadOnlyCollection<int> AllowedPitchAngles => _pitchFactors.Keys.Order().ToArray();

    public int ProtectivePitchAngle => _pitchFactors.Where(pair => pair.Value <= 0).Select(pair => pair.Key).Order().Last();

    public int ProductivePitchAngle => _pitchFactors.MaxBy(pair => pair.Value).Key;

    public static TurbineModel Parse(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new FormatException("Documentation is not a JSON object.");

        var anchors = new List<YieldAnchor>();
        double? damageAbove = null;

        foreach (var entry in root["windPowerYieldPercent"]?.AsArray() ?? [])
        {
            if (entry?.AsObject() is not { } item)
                continue;

            var yield = item["yieldPercent"]?.ToString() ?? string.Empty;

            if (item["windMs"] is { } single)
            {
                anchors.Add(new YieldAnchor(Number(single), ValueRange.Parse(yield)));
                continue;
            }

            var range = item["windMsRange"]?.ToString() ?? string.Empty;

            if (range.EndsWith('+'))
            {
                damageAbove = double.Parse(range.TrimEnd('+'), CultureInfo.InvariantCulture);
                continue;
            }

            // A range that yields a flat percentage becomes two anchors, so interpolation over the
            // whole curve stays one rule instead of a special case per entry.
            var bounds = ValueRange.Parse(range);
            anchors.Add(new YieldAnchor(bounds.Min, ValueRange.Parse(yield)));
            anchors.Add(new YieldAnchor(bounds.Max, ValueRange.Parse(yield)));
        }

        var pitchFactors = new Dictionary<int, double>();
        foreach (var entry in root["pitchAngleYieldPercent"]?.AsArray() ?? [])
        {
            if (entry?.AsObject() is not { } item)
                continue;

            pitchFactors[(int)Number(item["pitchAngleDeg"]!)] = ValueRange.Parse(item["yieldPercent"]!.ToString()).Max / 100d;
        }

        var safety = root["safety"]?.AsObject();
        var cutoff = safety?["cutoffWindMs"] is { } value ? Number(value) : damageAbove ?? anchors.Max(anchor => anchor.WindMs);
        var minimum = safety?["minOperationalWindMs"] is { } floor ? Number(floor) : anchors.Min(anchor => anchor.WindMs);

        return new TurbineModel(
            Number(root["ratedPowerKw"] ?? throw new FormatException("Documentation has no ratedPowerKw.")),
            Math.Min(cutoff, damageAbove ?? cutoff),
            minimum,
            anchors.OrderBy(anchor => anchor.WindMs).ToArray(),
            pitchFactors);
    }

    /// <summary>
    /// True when the wind would break the blades. The documentation contradicts itself at exactly
    /// the cutoff speed - the table still yields 100% at 14 m/s while the safety rule calls "14+"
    /// damage - so the boundary itself is treated as dangerous: protecting an hour costs nothing,
    /// losing the blades costs the mission.
    /// </summary>
    public bool IsDamaging(double windMs) => windMs >= CutoffWindMs;

    /// <summary>True only for the speed where the two sources disagree, so a plan can say so out loud.</summary>
    public bool IsAtDisputedCutoff(double windMs) => Math.Abs(windMs - CutoffWindMs) < 0.001;

    public ValueRange PowerKw(double windMs, int pitchAngle)
    {
        if (!_pitchFactors.TryGetValue(pitchAngle, out var pitchFactor))
            throw new ArgumentOutOfRangeException(nameof(pitchAngle), $"Pitch {pitchAngle} is not one of {string.Join(", ", AllowedPitchAngles)}.");

        if (windMs < MinOperationalWindMs || IsDamaging(windMs))
            return new ValueRange(0, 0);

        var yield = InterpolateYield(windMs);
        return new ValueRange(RatedPowerKw * yield.Min / 100d * pitchFactor, RatedPowerKw * yield.Max / 100d * pitchFactor);
    }

    /// <summary>
    /// The table gives yields at 4, 6, 8, 10 and 12-14 m/s; the forecast reports speeds like 6.6.
    /// Both ends of the published range are interpolated separately, so the uncertainty the
    /// documentation admits to survives into the decision instead of collapsing into one number.
    /// </summary>
    private ValueRange InterpolateYield(double windMs)
    {
        if (windMs <= _windCurve[0].WindMs)
            return _windCurve[0].Percent;

        if (windMs >= _windCurve[^1].WindMs)
            return _windCurve[^1].Percent;

        for (var i = 1; i < _windCurve.Count; i++)
        {
            var upper = _windCurve[i];
            if (windMs > upper.WindMs)
                continue;

            var lower = _windCurve[i - 1];
            var span = upper.WindMs - lower.WindMs;
            var position = span <= 0 ? 0 : (windMs - lower.WindMs) / span;

            return new ValueRange(
                lower.Percent.Min + (upper.Percent.Min - lower.Percent.Min) * position,
                lower.Percent.Max + (upper.Percent.Max - lower.Percent.Max) * position);
        }

        return _windCurve[^1].Percent;
    }

    private static double Number(JsonNode node) => double.Parse(node.ToString(), CultureInfo.InvariantCulture);
}
