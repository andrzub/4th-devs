using System.Text.Json;
using System.Text.Json.Serialization;
using _04_05_zadanie.Warehouse;

namespace _04_05_zadanie.Mission;

public sealed record ObservedDestination(int DestinationId, string Name);

public sealed record ObservedRole(int RoleId, string Name);

public sealed record ObservedUser(int? UserId, string? Login, string? NameSurname, string? Birthday, int? Role, int? IsActive)
{
    public string Describe() =>
        $"user_id={UserId?.ToString() ?? "NULL"} login={Login ?? "?"} birthday={Birthday ?? "?"} role={Role?.ToString() ?? "?"} is_active={IsActive?.ToString() ?? "?"}";
}

public sealed record AbsorbSummary(int Destinations, int Users, int Roles, int Unrecognised)
{
    public string Render()
    {
        var parts = new List<string>();
        if (Destinations > 0) parts.Add($"{Destinations} destination(s)");
        if (Users > 0) parts.Add($"{Users} user(s)");
        if (Roles > 0) parts.Add($"{Roles} role(s)");
        if (Unrecognised > 0) parts.Add($"{Unrecognised} row(s) not recorded (keep the original column names: destination_id, name, user_id, login, birthday, role, is_active)");
        return parts.Count == 0 ? "Nothing recorded from this result." : $"Recorded: {string.Join(", ", parts)}.";
    }
}

/// <summary>
/// What the run has actually read from the database and the order list. The plan validator
/// accepts a destination code or a creator only when it appears here, so a value the model typed
/// from memory or invented has no way into an order. Rows are recognised by their column names;
/// partial rows about the same user are merged, because the agent may select the columns in
/// several queries.
/// </summary>
public sealed class Observations
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly Dictionary<int, ObservedDestination> _destinations = new();
    private readonly Dictionary<int, ObservedRole> _roles = new();
    private readonly List<ObservedUser> _users = [];
    private readonly List<WarehouseOrder> _orders = [];

    public IReadOnlyCollection<ObservedDestination> Destinations => _destinations.Values;

    public IReadOnlyCollection<ObservedRole> Roles => _roles.Values;

    public IReadOnlyList<ObservedUser> Users => _users;

    /// <summary>The order list as last seen; a new listing replaces it, because it is the whole state.</summary>
    public IReadOnlyList<WarehouseOrder> Orders => _orders;

    public bool OrdersListed { get; private set; }

    public AbsorbSummary Absorb(QueryResult result)
    {
        int destinations = 0, users = 0, roles = 0, unrecognised = 0;

        foreach (var row in result.Rows)
        {
            if (row.ContainsKey("destination_id"))
            {
                var id = QueryResult.ReadInt(row, "destination_id");
                var name = QueryResult.ReadString(row, "name");
                if (id is { } destinationId && !string.IsNullOrWhiteSpace(name))
                {
                    _destinations[destinationId] = new ObservedDestination(destinationId, name);
                    destinations++;
                }
                else
                {
                    unrecognised++;
                }
            }
            else if (row.ContainsKey("role_id"))
            {
                var id = QueryResult.ReadInt(row, "role_id");
                var name = QueryResult.ReadString(row, "name");
                if (id is { } roleId && !string.IsNullOrWhiteSpace(name))
                {
                    _roles[roleId] = new ObservedRole(roleId, name);
                    roles++;
                }
                else
                {
                    unrecognised++;
                }
            }
            else if (row.ContainsKey("login") || row.ContainsKey("user_id"))
            {
                MergeUser(new ObservedUser(
                    QueryResult.ReadInt(row, "user_id"),
                    QueryResult.ReadString(row, "login"),
                    QueryResult.ReadString(row, "name_surname"),
                    QueryResult.ReadString(row, "birthday"),
                    QueryResult.ReadInt(row, "role"),
                    QueryResult.ReadInt(row, "is_active")));
                users++;
            }
            else
            {
                unrecognised++;
            }
        }

        return new AbsorbSummary(destinations, users, roles, unrecognised);
    }

    public void AbsorbOrders(IEnumerable<WarehouseOrder> orders)
    {
        _orders.Clear();
        _orders.AddRange(orders);
        OrdersListed = true;
    }

    public ObservedDestination? FindDestination(int destinationId) => _destinations.GetValueOrDefault(destinationId);

    public ObservedUser? FindUserById(int userId) => _users.FirstOrDefault(user => user.UserId == userId);

    public ObservedUser? FindUserByLogin(string login) => _users.FirstOrDefault(user => user.Login == login);

    public string DescribeRole(int? role) =>
        role is { } id ? (_roles.TryGetValue(id, out var known) ? $"{id} ({known.Name})" : id.ToString()) : "unknown";

    public string RenderSummary() =>
        $"Observed so far: {_destinations.Count} destination(s), {_users.Count} user(s), {_roles.Count} role(s), {(OrdersListed ? $"{_orders.Count} existing order(s)" : "orders not listed yet")}.";

    public string ToJson() => JsonSerializer.Serialize(new Snapshot([.. _destinations.Values], [.. _roles.Values], [.. _users], [.. _orders], OrdersListed), JsonOptions);

    public static Observations FromJson(string json)
    {
        var snapshot = JsonSerializer.Deserialize<Snapshot>(json, JsonOptions) ?? throw new FormatException("The observations file is empty.");
        var observations = new Observations();
        foreach (var destination in snapshot.Destinations)
            observations._destinations[destination.DestinationId] = destination;
        foreach (var role in snapshot.Roles)
            observations._roles[role.RoleId] = role;
        observations._users.AddRange(snapshot.Users);
        observations._orders.AddRange(snapshot.Orders);
        observations.OrdersListed = snapshot.OrdersListed;
        return observations;
    }

    /// <summary>Later rows fill in what earlier rows about the same user left out; a value that is present wins over null.</summary>
    private void MergeUser(ObservedUser incoming)
    {
        var index = incoming.Login is not null
            ? _users.FindIndex(user => user.Login == incoming.Login)
            : -1;
        if (index < 0 && incoming.UserId is { } userId)
            index = _users.FindIndex(user => user.UserId == userId && (user.Login is null || incoming.Login is null));

        if (index < 0)
        {
            _users.Add(incoming);
            return;
        }

        var existing = _users[index];
        _users[index] = new ObservedUser(
            incoming.UserId ?? existing.UserId,
            incoming.Login ?? existing.Login,
            incoming.NameSurname ?? existing.NameSurname,
            incoming.Birthday ?? existing.Birthday,
            incoming.Role ?? existing.Role,
            incoming.IsActive ?? existing.IsActive);
    }

    private sealed record Snapshot(List<ObservedDestination> Destinations, List<ObservedRole> Roles, List<ObservedUser> Users, List<WarehouseOrder> Orders, bool OrdersListed);
}
