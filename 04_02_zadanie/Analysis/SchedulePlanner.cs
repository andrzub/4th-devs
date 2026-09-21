namespace _04_02_zadanie.Analysis;

/// <summary>
/// Turns a forecast, the turbine's own power curve and the plant's deficit into the points that
/// will be signed and stored. Nothing here is a judgement call for a model: which hours break the
/// blades and which hour first covers the deficit are arithmetic over published numbers.
/// </summary>
public static class SchedulePlanner
{
    public static SchedulePlan Plan(WeatherForecast forecast, TurbineModel turbine, ValueRange deficitKw)
    {
        var notes = new List<string>();

        var protections = forecast.Entries
            .Where(entry => turbine.IsDamaging(entry.WindMs))
            .Select(entry => new ConfigPoint(entry.Timestamp, entry.WindMs, turbine.ProtectivePitchAngle, TurbineModes.Idle))
            .ToList();

        foreach (var entry in forecast.Entries.Where(entry => turbine.IsAtDisputedCutoff(entry.WindMs)))
            notes.Add($"{entry.Timestamp:yyyy-MM-dd HH:mm} sits exactly at the {turbine.CutoffWindMs} m/s cutoff, where the yield table and the safety rule disagree - protected on purpose.");

        // The hour has to be able to cover the largest reported deficit. Both the deficit and the
        // yield come as ranges, and demanding the weakest yield against the largest deficit rejects
        // hours the plant can in fact be started on: the forecast holds one or two usable hours in
        // a week, so an over-strict rule returns "impossible" where an answer plainly exists.
        var required = deficitKw.Max;
        bool CanCover(double windMs) => turbine.PowerKw(windMs, turbine.ProductivePitchAngle).Max >= required;

        var production = forecast.Entries
            .Where(entry => !turbine.IsDamaging(entry.WindMs))
            .Where(entry => CanCover(entry.WindMs))
            .OrderBy(entry => entry.Timestamp)
            .Select(entry => new ConfigPoint(entry.Timestamp, entry.WindMs, turbine.ProductivePitchAngle, TurbineModes.Production))
            .FirstOrDefault();

        if (production is null)
        {
            notes.Add($"No hour in the forecast produces {required:0.##} kW at pitch {turbine.ProductivePitchAngle} in the worst case.");
        }
        else
        {
            var power = turbine.PowerKw(production.WindMs, production.PitchAngle);
            notes.Add($"Production at {production.Key}: {power.Min:0.##}-{power.Max:0.##} kW against a deficit of {deficitKw} kW.");

            notes.Add(power.Min >= required
                ? $"The hour covers {required:0.##} kW even on the weakest published yield."
                : $"On the weakest published yield the hour delivers {power.Min:0.##} kW, short of {required:0.##} kW - it is chosen because it can reach {power.Max:0.##} kW and nothing earlier can.");

            var alternatives = turbine.AllowedPitchAngles
                .Where(angle => angle != production.PitchAngle)
                .Where(angle => turbine.PowerKw(production.WindMs, angle).Max >= required)
                .ToList();

            if (alternatives.Count == 0)
                notes.Add($"Pitch {production.PitchAngle} is the only angle that covers the deficit at {production.WindMs:0.#} m/s.");
        }

        var points = protections
            .Concat(production is null ? [] : new[] { production })
            .OrderBy(point => point.Timestamp)
            .ToList();

        return new SchedulePlan(points, production, protections, deficitKw, notes);
    }
}
