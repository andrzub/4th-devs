using System.Globalization;
using System.Text.Json.Nodes;

namespace _04_02_zadanie.Analysis;

public sealed record ForecastEntry(DateTime Timestamp, double WindMs, double PrecipitationMm, double TemperatureC);

public sealed class WeatherForecast(IReadOnlyList<ForecastEntry> entries, int intervalHours)
{
    public IReadOnlyList<ForecastEntry> Entries { get; } = entries;

    public int IntervalHours { get; } = intervalHours;

    public static WeatherForecast Parse(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new FormatException("Weather report is not a JSON object.");
        var forecast = root["forecast"]?.AsArray() ?? throw new FormatException("Weather report has no forecast array.");

        var entries = new List<ForecastEntry>();
        foreach (var node in forecast)
        {
            if (node?.AsObject() is not { } item)
                continue;

            entries.Add(new ForecastEntry(
                DateTime.ParseExact(item["timestamp"]!.ToString(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Number(item["windMs"]),
                Number(item["precipitationMm"]),
                Number(item["temperatureC"])));
        }

        var interval = root["intervalHours"] is { } hours ? (int)Number(hours) : 1;
        return new WeatherForecast(entries.OrderBy(entry => entry.Timestamp).ToArray(), interval);
    }

    /// <summary>
    /// True when another forecast reports the same wind at the same hours. The schedule is planned
    /// from a cached forecast so the signatures can be ordered before the fresh one arrives; this
    /// is what turns that shortcut into an assumption the run can check rather than trust.
    /// </summary>
    public bool MatchesWind(WeatherForecast other)
    {
        if (Entries.Count != other.Entries.Count)
            return false;

        return Entries.Zip(other.Entries).All(pair =>
            pair.First.Timestamp == pair.Second.Timestamp && Math.Abs(pair.First.WindMs - pair.Second.WindMs) < 0.001);
    }

    private static double Number(JsonNode? node) => node is null ? 0 : double.Parse(node.ToString(), CultureInfo.InvariantCulture);
}
