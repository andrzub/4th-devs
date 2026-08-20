using System.Text;
using System.Text.Json;

namespace _01_02_zadanie.Tools;

/// <summary>
/// Batch Haversine: for every sighting finds the nearest power plant.
/// The model supplies plant coordinates from its own knowledge; the math is deterministic,
/// so the agent cannot skip or eyeball any sighting-to-plant pair.
/// </summary>
internal class FindNearestPowerPlantTool : ITool
{
    public string Name => "find_nearest_power_plant";

    public string Description => "For each sighting (latitude/longitude) computes the Haversine distance to every provided power plant and returns the nearest plant per sighting plus the overall closest match. Use it once per suspect with all their sightings and the full plant list.";

    public JsonElement ParametersSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "sightings": {
              "type": "array",
              "description": "All sightings of one suspect.",
              "items": {
                "type": "object",
                "properties": {
                  "latitude": { "type": "number" },
                  "longitude": { "type": "number" }
                },
                "required": ["latitude", "longitude"]
              }
            },
            "powerPlants": {
              "type": "array",
              "description": "All power plants with their city-centre coordinates (from your own geographic knowledge).",
              "items": {
                "type": "object",
                "properties": {
                  "code": { "type": "string", "description": "Plant code, format PWR0000PL." },
                  "city": { "type": "string" },
                  "latitude": { "type": "number" },
                  "longitude": { "type": "number" }
                },
                "required": ["code", "city", "latitude", "longitude"]
              }
            }
          },
          "required": ["sightings", "powerPlants"]
        }
        """);

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        List<(double Lat, double Lon)> sightings = [];
        List<(string Code, string City, double Lat, double Lon)> plants = [];

        try
        {
            var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
            foreach (var s in args.GetProperty("sightings").EnumerateArray())
            {
                sightings.Add((s.GetProperty("latitude").GetDouble(), s.GetProperty("longitude").GetDouble()));
            }
            foreach (var p in args.GetProperty("powerPlants").EnumerateArray())
            {
                plants.Add((p.GetProperty("code").GetString() ?? "?", p.GetProperty("city").GetString() ?? "?",
                    p.GetProperty("latitude").GetDouble(), p.GetProperty("longitude").GetDouble()));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Invalid input ({ex.Message}). Provide 'sightings' and 'powerPlants' arrays with numeric latitude/longitude.");
        }

        if (sightings.Count == 0 || plants.Count == 0)
        {
            return Task.FromResult("Invalid input. Both 'sightings' and 'powerPlants' must be non-empty arrays.");
        }

        var sb = new StringBuilder();
        var bestDistance = double.MaxValue;
        var bestLine = "";

        foreach (var (lat, lon) in sightings)
        {
            var nearest = plants.MinBy(p => Haversine(lat, lon, p.Lat, p.Lon));
            var distance = Math.Round(Haversine(lat, lon, nearest.Lat, nearest.Lon));
            var line = $"sighting ({lat}, {lon}) -> nearest plant {nearest.Code} ({nearest.City}) at {distance} m";
            sb.AppendLine(line);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestLine = line;
            }
        }

        sb.AppendLine($"CLOSEST OVERALL: {bestLine}");
        return Task.FromResult(sb.ToString());
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371e3;
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaPhi = (lat2 - lat1) * Math.PI / 180;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
