using System.Text.Json;

namespace _01_02_zadanie.Tools;

/// <summary>
/// Sample tool: returns the current date and time, optionally in a requested timezone.
/// </summary>
public class GetCurrentDateTimeTool : ITool
{
    public string Name => "get_current_datetime";
    public string Description => "Returns the current date and time. Optionally accepts a timezone identifier (IANA format, e.g. 'Europe/Warsaw'). Defaults to UTC.";

    public JsonElement ParametersSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "timezone": {
              "type": "string",
              "description": "IANA timezone identifier, e.g. 'Europe/Warsaw'. Defaults to UTC if omitted."
            }
          },
          "required": []
        }
        """);

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        string? timezone = null;

        try
        {
            var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
            if (args.TryGetProperty("timezone", out var tz))
                timezone = tz.GetString();
        }
        catch
        {
            // ignore malformed arguments — fall back to UTC
        }

        DateTimeOffset now;

        if (!string.IsNullOrWhiteSpace(timezone))
        {
            try
            {
                var tzi = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tzi);
            }
            catch (TimeZoneNotFoundException)
            {
                now = DateTimeOffset.UtcNow;
                timezone = "UTC (requested timezone not found)";
            }
        }
        else
        {
            now = DateTimeOffset.UtcNow;
            timezone = "UTC";
        }

        var result = $"{now:yyyy-MM-dd HH:mm:ss zzz} ({timezone})";
        return Task.FromResult(result);
    }
}
