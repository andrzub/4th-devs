using System.Text;
using System.Text.RegularExpressions;
using _04_05_zadanie.Warehouse;

namespace _04_05_zadanie.Mission;

public sealed record ValidationReport(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;

    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine(IsValid ? "Plan valid: every check passed." : $"Plan NOT valid: {Errors.Count} problem(s).");
        foreach (var error in Errors)
            sb.AppendLine($"  ERROR    {error}");
        foreach (var warning in Warnings)
            sb.AppendLine($"  WARNING  {warning}");
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Judges the plan against the demand file and against what was actually observed. Errors block
/// execution: a city without an order, a code or a creator nobody read from the database, a
/// creator whose login or birthday differs from the row, an inactive creator. Warnings do not:
/// which role should sign an order is the agent's inference from the existing orders, not a rule
/// the documentation states, so a creator whose role differs is reported, not refused.
/// </summary>
public static partial class PlanValidator
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex BirthdayShape();

    public static ValidationReport Validate(OrderPlan plan, Demand demand, Observations observations)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var city in demand.Cities)
        {
            var count = plan.Orders.Count(order => TextNormalizer.SameName(order.City, city.City));
            if (count == 0)
                errors.Add($"No order for {city.City}.");
            else if (count > 1)
                errors.Add($"{city.City} has {count} orders; exactly one is expected.");
        }

        foreach (var order in plan.Orders)
        {
            if (demand.Find(order.City) is null)
                errors.Add($"'{order.City}' is not a city of the demand file; the cities are: {string.Join(", ", demand.Cities.Select(c => c.City))}.");

            errors.AddRange(OrderErrors(order, observations));
        }

        var seededRoles = SeededCreatorRoles(observations);
        if (!observations.OrdersListed)
            warnings.Add("The existing orders were never listed, so the creators' roles could not be compared with them.");
        else if (seededRoles.Count == 0 && observations.Orders.Count > 0)
            warnings.Add("None of the existing orders' creators was seen as a user row, so the creators' roles could not be compared with them.");

        foreach (var order in plan.Orders)
            warnings.AddRange(OrderWarnings(order, observations, seededRoles));

        return new ValidationReport(errors, warnings);
    }

    /// <summary>The hard checks of one order, usable before the plan is complete.</summary>
    public static IReadOnlyList<string> OrderErrors(PlannedOrder order, Observations observations)
    {
        var errors = new List<string>();
        var prefix = $"{order.City}: ";

        if (string.IsNullOrWhiteSpace(order.Title))
            errors.Add($"{prefix}the title is empty.");

        var destination = observations.FindDestination(order.DestinationId);
        if (destination is null)
            errors.Add($"{prefix}destination {order.DestinationId} was not seen in any query result. Select destination_id and name from destinations for this city first.");
        else if (!TextNormalizer.SameName(destination.Name, order.City))
            errors.Add($"{prefix}destination {order.DestinationId} is {destination.Name}, not {order.City}.");

        if (!BirthdayShape().IsMatch(order.Birthday ?? string.Empty))
            errors.Add($"{prefix}birthday '{order.Birthday}' is not in the YYYY-MM-DD form.");

        var user = observations.FindUserById(order.CreatorId);
        if (user is null)
        {
            errors.Add($"{prefix}creatorID {order.CreatorId} was not seen in any query result. Select user_id, login, birthday, role, is_active from users for the creator first.");
            return errors;
        }

        if (user.Login is null)
            errors.Add($"{prefix}the login of user_id {order.CreatorId} was never selected.");
        else if (user.Login != order.Login)
            errors.Add($"{prefix}login '{order.Login}' does not belong to user_id {order.CreatorId} (observed login: {user.Login}).");

        if (user.Birthday is null)
            errors.Add($"{prefix}the birthday of user_id {order.CreatorId} was never selected.");
        else if (user.Birthday != order.Birthday)
            errors.Add($"{prefix}birthday {order.Birthday} differs from the observed {user.Birthday} of user_id {order.CreatorId}.");

        if (user.IsActive is null)
            errors.Add($"{prefix}is_active of user_id {order.CreatorId} was never selected.");
        else if (user.IsActive != 1)
            errors.Add($"{prefix}user_id {order.CreatorId} is not active (is_active = {user.IsActive}).");

        return errors;
    }

    private static IReadOnlyList<string> OrderWarnings(PlannedOrder order, Observations observations, IReadOnlyList<int> seededRoles)
    {
        var user = observations.FindUserById(order.CreatorId);
        if (user is null)
            return [];

        if (user.Role is null)
            return [$"{order.City}: the role of user_id {order.CreatorId} was never selected, so it cannot be compared with the creators of the existing orders."];

        if (seededRoles.Count > 0 && !seededRoles.Contains(user.Role.Value))
            return [$"{order.City}: creator {user.Login ?? order.CreatorId.ToString()} has role {observations.DescribeRole(user.Role)}, while the creators of the existing orders have role(s) {string.Join(", ", seededRoles.Select(role => observations.DescribeRole(role)))}."];

        return [];
    }

    private static IReadOnlyList<int> SeededCreatorRoles(Observations observations) =>
        observations.Orders
            .Select(order => order.CreatorId is { } id ? observations.FindUserById(id)?.Role : null)
            .Where(role => role is not null)
            .Select(role => role!.Value)
            .Distinct()
            .ToList();
}
