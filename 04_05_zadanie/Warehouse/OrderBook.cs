using System.Text;
using System.Text.Json;

namespace _04_05_zadanie.Warehouse;

public sealed record WarehouseOrder(string Id, string Title, int? CreatorId, int? Destination, string? Signature, IReadOnlyDictionary<string, int> Items);

/// <summary>
/// The orders as the hub reports them. Parsing is tolerant about the shape (a list under "orders",
/// a single "order", or a bare order object) and about how items are written (a list of
/// name/items pairs or a name-to-quantity object), because only the list form has been observed.
/// </summary>
public static class OrderBook
{
    public static IReadOnlyList<WarehouseOrder> Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("The orders reply is not a JSON object.");

        if (root.TryGetProperty("orders", out var list) && list.ValueKind == JsonValueKind.Array)
            return list.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.Object).Select(ParseOrder).ToList();

        if (root.TryGetProperty("order", out var single) && single.ValueKind == JsonValueKind.Object)
            return [ParseOrder(single)];

        if (root.TryGetProperty("id", out _) && root.TryGetProperty("items", out _))
            return [ParseOrder(root)];

        return [];
    }

    /// <summary>Every way the order differs from the demand; empty when it matches exactly.</summary>
    public static IReadOnlyList<string> Differences(WarehouseOrder order, CityDemand demand)
    {
        var differences = new List<string>();

        foreach (var (name, wanted) in demand.Items)
        {
            if (!order.Items.TryGetValue(name, out var actual))
                differences.Add($"missing {name} {wanted}");
            else if (actual != wanted)
                differences.Add($"{name} is {actual}, should be {wanted}");
        }

        foreach (var (name, actual) in order.Items)
        {
            if (!demand.Items.ContainsKey(name))
                differences.Add($"extra {name} {actual}");
        }

        return differences;
    }

    public static string Render(IReadOnlyList<WarehouseOrder> orders)
    {
        if (orders.Count == 0)
            return "No orders.";

        var sb = new StringBuilder();
        sb.AppendLine($"{orders.Count} order(s):");
        foreach (var order in orders)
        {
            var items = order.Items.Count == 0 ? "(empty)" : string.Join(", ", order.Items.Select(i => $"{i.Key} {i.Value}"));
            sb.AppendLine($"  {order.Id}  \"{order.Title}\"  creatorID={Text(order.CreatorId)}  destination={Text(order.Destination)}  signature={order.Signature ?? "NULL"}");
            sb.AppendLine($"      items: {items}");
        }

        return sb.ToString().TrimEnd();
    }

    private static WarehouseOrder ParseOrder(JsonElement element)
    {
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        if (element.TryGetProperty("items", out var itemsElement))
        {
            if (itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in itemsElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
                {
                    var name = ReadString(entry, "name");
                    var quantity = ReadInt(entry, "items") ?? ReadInt(entry, "quantity");
                    if (name is not null && quantity is not null)
                        items[name] = items.GetValueOrDefault(name) + quantity.Value;
                }
            }
            else if (itemsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in itemsElement.EnumerateObject())
                {
                    if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var quantity))
                        items[entry.Name] = items.GetValueOrDefault(entry.Name) + quantity;
                }
            }
        }

        return new WarehouseOrder(
            ReadString(element, "id") ?? string.Empty,
            ReadString(element, "title") ?? string.Empty,
            ReadInt(element, "creatorID") ?? ReadInt(element, "creatorId"),
            ReadInt(element, "destination"),
            ReadString(element, "signature"),
            items);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            }
            : null;

    private static int? ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out var number) => number,
                JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
                _ => null
            }
            : null;

    private static string Text(int? value) => value?.ToString() ?? "NULL";
}
