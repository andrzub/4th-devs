using _04_05_zadanie.Hub;
using _04_05_zadanie.Warehouse;

namespace _04_05_zadanie.Mission;

/// <summary>
/// Re-reads from the live database exactly the rows a plan relies on and judges the plan against
/// them. The agent's observations are a snapshot; the orders are placed against the database as it
/// is now, so the check that gates --submit uses fresh rows, not the saved ones. Four read-only
/// requests: the roles, the existing orders, the planned destinations and the users involved.
/// </summary>
public static class PlanVerifier
{
    public static async Task<(Observations Observations, ValidationReport Report)> VerifyLiveAsync(FoodwarehouseClient hub, OrderPlan plan, Demand demand, CancellationToken cancellationToken = default)
    {
        var observations = new Observations();

        var roles = await hub.QueryAsync("select role_id, name from roles", cancellationToken);
        if (roles.IsSuccess)
            observations.Absorb(QueryResult.Parse(roles.Body));

        var orders = await hub.GetOrdersAsync(cancellationToken: cancellationToken);
        if (!orders.IsSuccess)
            throw new InvalidOperationException($"The existing orders could not be listed: {orders.Describe()}");
        observations.AbsorbOrders(OrderBook.Parse(orders.Body));

        if (plan.Orders.Count > 0)
        {
            var destinationIds = plan.Orders.Select(order => order.DestinationId).Distinct().OrderBy(id => id);
            var destinations = await hub.QueryAsync($"select destination_id, name from destinations where destination_id in ({string.Join(", ", destinationIds)})", cancellationToken);
            if (!destinations.IsSuccess)
                throw new InvalidOperationException($"The destinations could not be read: {destinations.Describe()}");
            observations.Absorb(QueryResult.Parse(destinations.Body));

            var userIds = plan.Orders.Select(order => order.CreatorId)
                .Concat(observations.Orders.Select(order => order.CreatorId ?? -1).Where(id => id >= 0))
                .Distinct()
                .OrderBy(id => id);
            var users = await hub.QueryAsync($"select user_id, login, name_surname, birthday, role, is_active from users where user_id in ({string.Join(", ", userIds)})", cancellationToken);
            if (!users.IsSuccess)
                throw new InvalidOperationException($"The users could not be read: {users.Describe()}");
            observations.Absorb(QueryResult.Parse(users.Body));
        }

        return (observations, PlanValidator.Validate(plan, demand, observations));
    }
}
