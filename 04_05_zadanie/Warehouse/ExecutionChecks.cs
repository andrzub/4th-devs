using System.Text.Json;
using System.Text.RegularExpressions;
using _04_05_zadanie.Mission;

namespace _04_05_zadanie.Warehouse;

/// <summary>
/// The pure parts of placing orders: reading a signature or a new order's id out of a reply,
/// finding a planned order in the warehouse's list, and judging the final state. Kept apart from
/// the executor so they can be checked offline against replies of every shape the hub may use.
/// </summary>
public static partial class ExecutionChecks
{
    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.IgnoreCase)]
    private static partial Regex Sha1();

    public static string? ParseSignature(string body)
    {
        using var doc = TryParse(body);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var name in new[] { "hash", "signature" })
        {
            if (doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && Sha1().IsMatch(value.GetString()!))
                return value.GetString()!.ToLowerInvariant();
        }

        return null;
    }

    public static string? ParseCreatedId(string body)
    {
        using var doc = TryParse(body);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Object)
            return null;

        var candidates = new List<JsonElement> { doc.RootElement };
        if (doc.RootElement.TryGetProperty("order", out var nested) && nested.ValueKind == JsonValueKind.Object)
            candidates.Add(nested);

        foreach (var candidate in candidates)
        {
            foreach (var name in new[] { "id", "orderID", "orderId", "order_id" })
            {
                if (!candidate.TryGetProperty(name, out var value))
                    continue;
                if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                    return value.GetString()!.Trim();
                if (value.ValueKind == JsonValueKind.Number)
                    return value.GetRawText();
            }
        }

        return null;
    }

    /// <summary>The warehouse order that carries the planned destination, creator and signature.</summary>
    public static WarehouseOrder? FindOrder(IReadOnlyList<WarehouseOrder> orders, PlannedOrder planned, string signature) =>
        orders.FirstOrDefault(order =>
            order.Destination == planned.DestinationId
            && order.CreatorId == planned.CreatorId
            && string.Equals(order.Signature, signature, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The orders addressed to a destination the plan uses. The seeded orders go elsewhere and cannot
    /// be deleted (delete reports success, the listing keeps them), so only these are cleared before
    /// placing and judged afterwards.
    /// </summary>
    public static IReadOnlyList<WarehouseOrder> OrdersToPlannedDestinations(IReadOnlyList<WarehouseOrder> orders, OrderPlan plan)
    {
        var destinations = plan.Orders.Select(order => order.DestinationId).ToHashSet();
        return orders.Where(order => order.Destination is { } destination && destinations.Contains(destination)).ToList();
    }

    /// <summary>
    /// Every way the warehouse's final list differs from the plan and the demand; empty when it is
    /// exactly right. Orders to destinations outside the plan are not judged.
    /// </summary>
    public static IReadOnlyList<string> VerifyFinal(IReadOnlyList<WarehouseOrder> orders, OrderPlan plan, Demand demand, IReadOnlyDictionary<string, string> signaturesByCity)
    {
        var problems = new List<string>();
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var relevant = OrdersToPlannedDestinations(orders, plan);

        if (relevant.Count != plan.Orders.Count)
            problems.Add($"{relevant.Count} order(s) to the planned destinations in the warehouse, {plan.Orders.Count} planned.");

        foreach (var planned in plan.Orders)
        {
            if (!signaturesByCity.TryGetValue(planned.City, out var signature))
            {
                problems.Add($"{planned.City}: no signature was generated.");
                continue;
            }

            var order = FindOrder(orders, planned, signature);
            if (order is null)
            {
                problems.Add($"{planned.City}: no order with destination {planned.DestinationId}, creatorID {planned.CreatorId} and the generated signature.");
                continue;
            }

            matched.Add(order.Id);

            var city = demand.Find(planned.City);
            if (city is null)
            {
                problems.Add($"{planned.City}: not in the demand list.");
                continue;
            }

            foreach (var difference in OrderBook.Differences(order, city))
                problems.Add($"{planned.City} ({order.Id}): {difference}.");
        }

        foreach (var stray in relevant.Where(order => !matched.Contains(order.Id)))
            problems.Add($"Unexpected order {stray.Id} \"{stray.Title}\" to planned destination {stray.Destination}.");

        return problems;
    }

    private static JsonDocument? TryParse(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
