namespace _03_01_zadanie.Sensors;

/// <summary>
/// One measurement channel: the name that appears in <c>sensor_type</c>, the JSON field it
/// fills, and the range the task defines as healthy. An inactive channel must report exactly 0.
/// </summary>
public sealed record SensorChannel(string Name, string JsonField, double Min, double Max, string Unit)
{
    public bool IsWithinRange(double value) => value >= Min && value <= Max;

    public string DescribeRange() => $"{Min:0.###}..{Max:0.###} {Unit}";

    public static readonly IReadOnlyList<SensorChannel> All = new[]
    {
        new SensorChannel("temperature", "temperature_K",      553,  873,  "K"),
        new SensorChannel("pressure",    "pressure_bar",        60,  160,  "bar"),
        new SensorChannel("water",       "water_level_meters",   5.0, 15.0, "m"),
        new SensorChannel("voltage",     "voltage_supply_v",   229.0, 231.0, "V"),
        new SensorChannel("humidity",    "humidity_percent",    40.0, 80.0, "%")
    };

    public static SensorChannel? ByName(string name) =>
        All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}
