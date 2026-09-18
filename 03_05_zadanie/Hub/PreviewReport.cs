using System.Globalization;
using System.Text;
using System.Text.Json;

namespace _03_05_zadanie.Hub;

/// <summary>
/// Turns the preview state into a readable post-mortem of the last submitted route: where the
/// traveller stood after every step, in what mode, and with how much fuel and food left. The hub's
/// own rejection names one problem; this timeline shows the step it happened on.
/// </summary>
public static class PreviewReport
{
    public static string Render(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return body;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("map", out var mapElement) || mapElement.ValueKind != JsonValueKind.Array)
                return Text(root, "message") is { Length: > 0 } message ? message : body;

            var sb = new StringBuilder();
            sb.AppendLine($"updated {Text(root, "updated_at")}  reached_goal: {Flag(root, "reached_goal")}");

            foreach (var row in mapElement.EnumerateArray())
                sb.AppendLine(string.Concat(row.EnumerateArray().Select(cell => cell.ToString())));

            if (root.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
                sb.AppendLine($"steps: {string.Join(" ", steps.EnumerateArray().Select(step => step.ToString()))}");

            if (root.TryGetProperty("timeline", out var timeline) && timeline.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in timeline.EnumerateArray())
                {
                    var position = entry.TryGetProperty("player", out var player)
                        ? $"({Text(player, "row")},{Text(player, "col")})"
                        : "(?,?)";
                    var resources = entry.TryGetProperty("resources", out var res)
                        ? $"fuel {Text(res, "fuel"),5}  food {Text(res, "food"),5}"
                        : string.Empty;

                    sb.AppendLine($"step {Text(entry, "step"),3}  {Text(entry, "mode"),-6} {position,-8} {Text(entry, "status"),-8} {resources}  {Text(entry, "message")}");
                }
            }

            var finalMessage = Text(root, "message");
            if (finalMessage.Length > 0)
                sb.AppendLine(finalMessage);

            return sb.ToString().TrimEnd();
        }
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString()!,
                JsonValueKind.Number => value.GetDouble().ToString("0.###", CultureInfo.InvariantCulture),
                JsonValueKind.Null => string.Empty,
                _ => value.ToString()
            }
            : string.Empty;

    private static string Flag(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True ? "yes" : "no";
}
