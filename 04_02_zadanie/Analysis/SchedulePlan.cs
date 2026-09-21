using System.Globalization;

namespace _04_02_zadanie.Analysis;

public static class TurbineModes
{
    public const string Production = "production";

    public const string Idle = "idle";
}

public sealed record ConfigPoint(DateTime Timestamp, double WindMs, int PitchAngle, string TurbineMode)
{
    public string StartDate => Timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public string StartHour => Timestamp.ToString("HH:00:00", CultureInfo.InvariantCulture);

    /// <summary>The key the batch form of "config" expects, and the identity used to match a signature to its point.</summary>
    public string Key => Timestamp.ToString("yyyy-MM-dd HH:00:00", CultureInfo.InvariantCulture);

    public override string ToString() => $"{Key}  {WindMs,5:0.#} m/s  pitch {PitchAngle,2}  {TurbineMode}";
}

public sealed record SchedulePlan(
    IReadOnlyList<ConfigPoint> Points,
    ConfigPoint? Production,
    IReadOnlyList<ConfigPoint> Protections,
    ValueRange Deficit,
    IReadOnlyList<string> Notes);
