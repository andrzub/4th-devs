namespace _04_02_zadanie.Analysis;

public sealed record ScheduleVerdict(bool Accepted, IReadOnlyList<string> Problems)
{
    public static ScheduleVerdict Ok { get; } = new(true, []);
}

/// <summary>
/// The last check before the window's one and only "config". A schedule that is wrong costs the
/// whole service window, and there is no time inside it to read an error and think again - so the
/// shape of the answer is settled here, offline, against the same forecast the points came from.
/// </summary>
public static class ScheduleValidator
{
    public static ScheduleVerdict Validate(SchedulePlan plan, WeatherForecast forecast, TurbineModel turbine, IReadOnlyDictionary<string, string>? signatures = null)
    {
        var problems = new List<string>();

        var damagingHours = forecast.Entries.Where(entry => turbine.IsDamaging(entry.WindMs)).ToList();
        var byKey = plan.Points.ToLookup(point => point.Key);

        foreach (var hour in damagingHours)
        {
            var key = hour.Timestamp.ToString("yyyy-MM-dd HH:00:00");
            var point = byKey[key].FirstOrDefault();

            if (point is null)
                problems.Add($"{key} blows {hour.WindMs:0.#} m/s and has no configuration point.");
            else if (point.PitchAngle != turbine.ProtectivePitchAngle || point.TurbineMode != TurbineModes.Idle)
                problems.Add($"{key} is a storm but is configured as pitch {point.PitchAngle} / {point.TurbineMode}.");
        }

        if (plan.Production is null)
        {
            problems.Add("No production point: the plant would stay in standby.");
        }
        else
        {
            var production = plan.Production;

            if (turbine.IsDamaging(production.WindMs))
                problems.Add($"Production is scheduled at {production.Key}, in {production.WindMs:0.#} m/s of wind - that breaks the blades.");

            if (production.TurbineMode != TurbineModes.Production)
                problems.Add($"Production point {production.Key} carries mode '{production.TurbineMode}'.");

            var power = turbine.PowerKw(production.WindMs, production.PitchAngle);
            if (power.Max < plan.Deficit.Max)
                problems.Add($"Production point {production.Key} reaches {power.Max:0.##} kW at best, below the {plan.Deficit.Max:0.##} kW deficit.");

            var forecastWind = forecast.Entries.FirstOrDefault(entry => entry.Timestamp == production.Timestamp);
            if (forecastWind is null)
                problems.Add($"Production point {production.Key} is not an hour the forecast reports.");
            else if (Math.Abs(forecastWind.WindMs - production.WindMs) > 0.001)
                problems.Add($"Production point {production.Key} was signed for {production.WindMs:0.#} m/s but the forecast says {forecastWind.WindMs:0.#} m/s.");

            var earlier = forecast.Entries
                .Where(entry => entry.Timestamp < production.Timestamp && !turbine.IsDamaging(entry.WindMs))
                .Where(entry => turbine.PowerKw(entry.WindMs, turbine.ProductivePitchAngle).Max >= plan.Deficit.Max)
                .ToList();

            if (earlier.Count > 0)
                problems.Add($"{earlier[0].Timestamp:yyyy-MM-dd HH:mm} would already cover the deficit - the centre asked for the first possible moment.");
        }

        foreach (var point in plan.Points)
        {
            if (!turbine.AllowedPitchAngles.Contains(point.PitchAngle))
                problems.Add($"{point.Key} uses pitch {point.PitchAngle}, which the turbine does not accept.");

            if (point.Timestamp.Minute != 0 || point.Timestamp.Second != 0)
                problems.Add($"{point.Key} is not a full hour; the API wants minutes and seconds at zero.");

            if (signatures is not null && (!signatures.TryGetValue(point.Key, out var code) || string.IsNullOrWhiteSpace(code)))
                problems.Add($"{point.Key} has no unlockCode.");
        }

        var duplicates = plan.Points.GroupBy(point => point.Key).Where(group => group.Count() > 1).Select(group => group.Key);
        foreach (var duplicate in duplicates)
            problems.Add($"{duplicate} appears more than once; the batch form would keep only one of them.");

        return problems.Count == 0 ? ScheduleVerdict.Ok : new ScheduleVerdict(false, problems);
    }
}
