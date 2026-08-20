using System.Text.Json;

namespace _01_02_zadanie.Tools;

internal class GetDistanceBetweenPointsTool : ITool
{
    public string Name => "get_distance_between_points";

    public string Description => "Calculates the distance between two geographical points (latitude and longitude).";

    public JsonElement ParametersSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "latitudeA": { "type": "number", "description": "Latitude of point A in degrees." },
            "longitudeA": { "type": "number", "description": "Longitude of point A in degrees." },
            "latitudeB": { "type": "number", "description": "Latitude of point B in degrees." },
            "longitudeB": { "type": "number", "description": "Longitude of point B in degrees." }
          },
          "required": ["latitudeA", "longitudeA", "latitudeB", "longitudeB"]
        }
        """);

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        double? latitudeA = null;
        double? longitudeA = null;
        double? latitudeB = null;
        double? longitudeB = null;

        try
        {
            var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
            if (args.TryGetProperty("latitudeA", out var latA))
            {
                latitudeA = latA.GetDouble();
            }
            if (args.TryGetProperty("longitudeA", out var lonA))
            {
                longitudeA = lonA.GetDouble();
            }
            if (args.TryGetProperty("latitudeB", out var latB))
            {
                latitudeB = latB.GetDouble();
            }
            if (args.TryGetProperty("longitudeB", out var lonB))
            {
                longitudeB = lonB.GetDouble();
            }
        }
        catch
        {
            // ignore malformed arguments
        }

        // calculate distance using Haversine formula
        if (latitudeA.HasValue && longitudeA.HasValue && latitudeB.HasValue && longitudeB.HasValue)
        {
            var R = 6371e3; // Earth radius in meters
            var phi1 = latitudeA.Value * Math.PI / 180;
            var phi2 = latitudeB.Value * Math.PI / 180;
            var deltaPhi = (latitudeB.Value - latitudeA.Value) * Math.PI / 180;
            var deltaLambda = (longitudeB.Value - longitudeA.Value) * Math.PI / 180;
            var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                    Math.Cos(phi1) * Math.Cos(phi2) *
                    Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            var distance = R * c; // in meters

            return Task.FromResult($"{Math.Round(distance)} meters");
        }
        else
        {
            return Task.FromResult("Invalid input. Please provide valid latitude and longitude for both points.");
        }
    }
}
