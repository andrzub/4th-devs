using System.Text;
using System.Text.Json;
using _04_05_zadanie.Warehouse;

namespace _04_05_zadanie.Mission;

/// <summary>
/// One order the agent intends to place: who signs it and where it goes. What goes into it is not
/// here on purpose; the items come from the demand file at execution time.
/// </summary>
public sealed record PlannedOrder(string City, string Title, int DestinationId, int CreatorId, string Login, string Birthday);

/// <summary>The agent's plan, one order per city, replaced city by city as the agent refines it.</summary>
public sealed class OrderPlan
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly List<PlannedOrder> _orders = [];

    public IReadOnlyList<PlannedOrder> Orders => _orders;

    public static string DefaultTitle(string city) => $"Dostawa zaopatrzenia: {Capitalise(TextNormalizer.Fold(city))}";

    /// <summary>Adds the order, replacing any earlier order for the same city.</summary>
    public void Upsert(PlannedOrder order)
    {
        var normalised = order with { City = TextNormalizer.Fold(order.City), Title = string.IsNullOrWhiteSpace(order.Title) ? DefaultTitle(order.City) : order.Title.Trim() };
        _orders.RemoveAll(existing => TextNormalizer.SameName(existing.City, normalised.City));
        _orders.Add(normalised);
    }

    public bool Remove(string city) => _orders.RemoveAll(existing => TextNormalizer.SameName(existing.City, city)) > 0;

    public PlannedOrder? Find(string city) => _orders.FirstOrDefault(existing => TextNormalizer.SameName(existing.City, city));

    public string ToJson() => JsonSerializer.Serialize(_orders, JsonOptions);

    public static OrderPlan FromJson(string json)
    {
        var orders = JsonSerializer.Deserialize<List<PlannedOrder>>(json, JsonOptions) ?? throw new FormatException("The plan file is empty.");
        var plan = new OrderPlan();
        foreach (var order in orders)
            plan.Upsert(order);
        return plan;
    }

    public string Render()
    {
        if (_orders.Count == 0)
            return "Plan: no orders registered yet.";

        var sb = new StringBuilder();
        sb.AppendLine($"Plan: {_orders.Count} order(s)");
        foreach (var order in _orders)
            sb.AppendLine($"  {order.City}: destination={order.DestinationId} creatorID={order.CreatorId} login={order.Login} birthday={order.Birthday} title=\"{order.Title}\"");
        return sb.ToString().TrimEnd();
    }

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
