using _04_05_zadanie.Hub;
using _04_05_zadanie.Mission;

namespace _04_05_zadanie.Warehouse;

/// <summary>
/// Places the orders of a verified plan, deterministically and idempotently: reset, clear whatever
/// the warehouse holds, then for each city a signature, a create and one batch append, and finally
/// one listing compared with the plan and the demand. A create or append whose reply was lost is
/// resolved by reading the warehouse back, never by sending it again blindly, because a repeated
/// append doubles the quantity. The run stops before done; that verdict is asked for separately.
/// </summary>
public sealed class OrderExecutor(FoodwarehouseClient hub, Demand demand)
{
    public IReadOnlyDictionary<string, string> Signatures => _signatures;

    private readonly Dictionary<string, string> _signatures = new(StringComparer.Ordinal);

    public async Task<bool> ExecuteAsync(OrderPlan plan, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("=== reset ===");
        var reset = await hub.ResetAsync(cancellationToken);
        Console.WriteLine(reset.Describe());
        if (!reset.IsSuccess)
            return Fail("Reset failed; nothing was placed.");

        if (!await ClearPlannedDestinationsAsync(plan, cancellationToken))
            return false;

        foreach (var city in demand.Cities)
        {
            var planned = plan.Find(city.City);
            if (planned is null)
                return Fail($"The plan has no order for {city.City}.");

            Console.WriteLine();
            Console.WriteLine($"=== {city.City}: {string.Join(", ", city.Items.Select(i => $"{i.Key} {i.Value}"))} ===");

            var signature = await GenerateSignatureAsync(planned, cancellationToken);
            if (signature is null)
                return false;
            _signatures[planned.City] = signature;

            var id = await CreateAsync(planned, signature, cancellationToken);
            if (id is null)
                return false;

            if (!await AppendAsync(id, city, cancellationToken))
                return false;
        }

        Console.WriteLine();
        Console.WriteLine("=== final check ===");
        var listing = await hub.GetOrdersAsync(cancellationToken: cancellationToken);
        if (!listing.IsSuccess)
            return Fail($"The orders could not be listed for the final check: {listing.Describe()}");

        var orders = OrderBook.Parse(listing.Body);
        Console.WriteLine(OrderBook.Render(orders));
        Console.WriteLine();

        var problems = ExecutionChecks.VerifyFinal(orders, plan, demand, _signatures);
        if (problems.Count > 0)
        {
            foreach (var problem in problems)
                Console.WriteLine($"  PROBLEM  {problem}");
            return Fail("The warehouse does not match the plan; do not call done. Fix the plan and submit again (submit starts with reset).");
        }

        var placed = ExecutionChecks.OrdersToPlannedDestinations(orders, plan).Count;
        Console.WriteLine($"All {placed} planned orders match the plan and the demand exactly; {orders.Count - placed} order(s) to other destinations left in place.");
        return true;
    }

    /// <summary>
    /// Removes any order already addressed to a planned destination, so that each city ends up with
    /// exactly one. The seeded orders go to other cities and are left alone: they cannot be deleted
    /// anyway (delete reports success and the listing keeps them).
    /// </summary>
    private async Task<bool> ClearPlannedDestinationsAsync(OrderPlan plan, CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine("=== clearing orders to the planned destinations ===");
        var listing = await hub.GetOrdersAsync(cancellationToken: cancellationToken);
        if (!listing.IsSuccess)
            return Fail($"The existing orders could not be listed: {listing.Describe()}");

        var existing = OrderBook.Parse(listing.Body);
        var toClear = ExecutionChecks.OrdersToPlannedDestinations(existing, plan);
        foreach (var order in toClear)
        {
            var reply = await hub.DeleteOrderAsync(order.Id, cancellationToken);
            Console.WriteLine($"delete {order.Id} \"{order.Title}\" (destination {order.Destination}) -> {reply.Status} {reply.Message ?? reply.Body}");
            if (!reply.IsSuccess)
                return Fail("An order to a planned destination could not be deleted.");
        }

        if (toClear.Count > 0)
        {
            var check = await hub.GetOrdersAsync(cancellationToken: cancellationToken);
            var remaining = check.IsSuccess ? ExecutionChecks.OrdersToPlannedDestinations(OrderBook.Parse(check.Body), plan) : null;
            if (remaining is null || remaining.Count > 0)
                return Fail($"{remaining?.Count.ToString() ?? "An unknown number of"} order(s) to the planned destinations remain after deleting; a second order for the same city cannot be avoided.");
        }

        Console.WriteLine($"{toClear.Count} order(s) to the planned destinations removed; {existing.Count - toClear.Count} order(s) to other destinations left in place.");
        return true;
    }

    private async Task<string?> GenerateSignatureAsync(PlannedOrder planned, CancellationToken cancellationToken)
    {
        var reply = await hub.GenerateSignatureAsync(planned.Login, planned.Birthday, planned.DestinationId, cancellationToken);
        var signature = reply.IsSuccess ? ExecutionChecks.ParseSignature(reply.Body) : null;
        Console.WriteLine($"signature for {planned.Login} -> {planned.DestinationId}: {(signature is null ? reply.Describe() : signature)}");
        if (signature is null)
            Fail("No SHA1 signature in the generator's reply.");
        return signature;
    }

    /// <summary>Creates the order and returns its id. A lost reply is settled by looking for the order in the warehouse.</summary>
    private async Task<string?> CreateAsync(PlannedOrder planned, string signature, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            HubReply reply;
            try
            {
                reply = await hub.CreateOrderAsync(planned.Title, planned.CreatorId, planned.DestinationId, signature, cancellationToken);
            }
            catch (HubTransportException ex)
            {
                Console.WriteLine($"create: {ex.Message}");
                var found = await FindInWarehouseAsync(planned, signature, cancellationToken);
                if (found is not null)
                {
                    Console.WriteLine($"create: the order exists after all, id {found.Id}.");
                    return found.Id;
                }

                Console.WriteLine("create: the order is not in the warehouse; sending it again.");
                continue;
            }

            Console.WriteLine($"create \"{planned.Title}\" creatorID={planned.CreatorId} destination={planned.DestinationId} -> {reply.Status} {reply.Message ?? string.Empty}");
            if (!reply.IsSuccess)
            {
                Console.WriteLine(reply.Body);
                Fail("The warehouse refused the order.");
                return null;
            }

            var id = ExecutionChecks.ParseCreatedId(reply.Body) ?? (await FindInWarehouseAsync(planned, signature, cancellationToken))?.Id;
            if (id is null)
            {
                Console.WriteLine(reply.Body);
                Fail("The created order's id could not be determined.");
                return null;
            }

            Console.WriteLine($"order id {id}");
            return id;
        }

        Fail("The order could not be created.");
        return null;
    }

    /// <summary>Appends every item in one batch. A lost reply is settled by reading the order back.</summary>
    private async Task<bool> AppendAsync(string id, CityDemand city, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            HubReply reply;
            try
            {
                reply = await hub.AppendItemsAsync(id, city.Items, cancellationToken);
            }
            catch (HubTransportException ex)
            {
                Console.WriteLine($"append: {ex.Message}");
                var order = await ReadOrderAsync(id, cancellationToken);
                if (order is null)
                    return Fail("The order could not be read back after the lost append; stopping rather than risking doubled quantities.");
                if (OrderBook.Differences(order, city).Count == 0)
                {
                    Console.WriteLine("append: the items are all there after all.");
                    return true;
                }
                if (order.Items.Count == 0)
                {
                    Console.WriteLine("append: the order is still empty; sending the batch again.");
                    continue;
                }

                return Fail($"The order holds a partial result ({string.Join(", ", order.Items.Select(i => $"{i.Key} {i.Value}"))}); stopping rather than appending on top of it.");
            }

            Console.WriteLine($"append {city.Items.Count} item(s) -> {reply.Status} {reply.Message ?? string.Empty}");
            if (!reply.IsSuccess)
            {
                Console.WriteLine(reply.Body);
                return Fail("The warehouse refused the items.");
            }

            return true;
        }

        return Fail("The items could not be appended.");
    }

    private async Task<WarehouseOrder?> FindInWarehouseAsync(PlannedOrder planned, string signature, CancellationToken cancellationToken)
    {
        var listing = await hub.GetOrdersAsync(cancellationToken: cancellationToken);
        return listing.IsSuccess ? ExecutionChecks.FindOrder(OrderBook.Parse(listing.Body), planned, signature) : null;
    }

    private async Task<WarehouseOrder?> ReadOrderAsync(string id, CancellationToken cancellationToken)
    {
        var reply = await hub.GetOrdersAsync(id, cancellationToken);
        if (!reply.IsSuccess)
            return null;

        // The hub may answer a get-by-id with the one order or with the whole list.
        return OrderBook.Parse(reply.Body).FirstOrDefault(order => order.Id == id);
    }

    private static bool Fail(string message)
    {
        Console.WriteLine($"STOP: {message}");
        return false;
    }
}
