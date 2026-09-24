using System.Text;
using System.Text.Json;
using _04_05_zadanie.Mission;

namespace _04_05_zadanie.Tools;

/// <summary>
/// Where the agent's conclusions become the plan. Each entry is judged on its own before it is
/// stored: a code or a creator that was never read from the database is refused with the reason,
/// so the plan only ever holds values with an observation behind them. Items are not part of an
/// entry; the demand file supplies them at execution time.
/// </summary>
public sealed class RegisterOrdersTool(MissionState state) : ITool
{
    public const string ToolName = "register_orders";

    public string Name => ToolName;

    public string Description =>
        "Registers one order per city in the plan: the city (as named in the demand list), its numeric destination_id, and the creator's user_id, login and birthday " +
        "exactly as read from the database. Registering a city again replaces its earlier entry. Entries whose values were not read from the database are refused. " +
        "The items and quantities are appended by code from the demand list; do not pass them.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "orders": {
              "type": "array",
              "description": "One entry per city; several cities can be registered in one call.",
              "items": {
                "type": "object",
                "properties": {
                  "city": { "type": "string", "description": "City name as it appears in the demand list." },
                  "destination_id": { "type": "integer", "description": "The city's numeric destination code from the destinations table." },
                  "creator_id": { "type": "integer", "description": "user_id of the user who creates the order." },
                  "login": { "type": "string", "description": "That user's login, as stored." },
                  "birthday": { "type": "string", "description": "That user's birthday, YYYY-MM-DD, as stored." },
                  "title": { "type": "string", "description": "Optional order title; a default is used when omitted." }
                },
                "required": ["city", "destination_id", "creator_id", "login", "birthday"],
                "additionalProperties": false
              }
            }
          },
          "required": ["orders"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        List<JsonElement> entries;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            entries = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("orders", out var orders) && orders.ValueKind == JsonValueKind.Array
                ? orders.EnumerateArray().Select(e => e.Clone()).ToList()
                : [];
        }
        catch (JsonException ex)
        {
            return Task.FromResult($"Refused: the arguments are not valid JSON ({ex.Message}).");
        }

        if (entries.Count == 0)
            return Task.FromResult("Refused: 'orders' must be a non-empty array of entries.");

        var sb = new StringBuilder();
        var accepted = 0;
        foreach (var entry in entries)
        {
            var outcome = Register(entry);
            if (outcome is null)
                accepted++;
            else
                sb.AppendLine(outcome);
        }

        var refused = entries.Count - accepted;
        sb.Insert(0, $"Accepted {accepted} entr{(accepted == 1 ? "y" : "ies")}, refused {refused}.{Environment.NewLine}");
        sb.AppendLine(state.Plan.Render());
        return Task.FromResult(sb.ToString().TrimEnd());
    }

    /// <summary>Stores the entry and returns null, or returns why it was refused.</summary>
    private string? Register(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            state.CountRegistration(refused: true);
            return "Refused an entry that is not an object.";
        }

        var city = ReadString(entry, "city");
        var destinationId = ReadInt(entry, "destination_id");
        var creatorId = ReadInt(entry, "creator_id");
        var login = ReadString(entry, "login");
        var birthday = ReadString(entry, "birthday");
        var title = ReadString(entry, "title");

        var label = string.IsNullOrWhiteSpace(city) ? "(no city)" : city.Trim();
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(city)) missing.Add("city");
        if (destinationId is null) missing.Add("destination_id (integer)");
        if (creatorId is null) missing.Add("creator_id (integer)");
        if (string.IsNullOrWhiteSpace(login)) missing.Add("login");
        if (string.IsNullOrWhiteSpace(birthday)) missing.Add("birthday");
        if (missing.Count > 0)
        {
            state.CountRegistration(refused: true);
            return $"Refused {label}: missing or mistyped {string.Join(", ", missing)}.";
        }

        if (state.Demand.Find(city) is not { } demandCity)
        {
            state.CountRegistration(refused: true);
            return $"Refused {label}: not a city of the demand list ({string.Join(", ", state.Demand.Cities.Select(c => c.City))}).";
        }

        var order = new PlannedOrder(demandCity.City, title ?? string.Empty, destinationId!.Value, creatorId!.Value, login!.Trim(), birthday!.Trim());
        var errors = PlanValidator.OrderErrors(order with { Title = string.IsNullOrWhiteSpace(order.Title) ? OrderPlan.DefaultTitle(order.City) : order.Title }, state.Observations);
        if (errors.Count > 0)
        {
            state.CountRegistration(refused: true);
            return $"Refused {label}: {string.Join(" ", errors)}";
        }

        state.Plan.Upsert(order);
        state.CountRegistration(refused: false);
        return null;
    }

    private static string? ReadString(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            }
            : null;

    private static int? ReadInt(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out var number) => number,
                JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
                _ => null
            }
            : null;
}
