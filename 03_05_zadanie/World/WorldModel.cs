using System.Globalization;
using System.Text;
using System.Text.Json;

namespace _03_05_zadanie.World;

public sealed record VehicleProfile(string Name, double FuelPerMove, double FoodPerMove, bool CanEnterWater, bool SelectableAtStart)
{
    public bool IsPowered => FuelPerMove > 0;
}

public enum TerrainKind
{
    Open,
    Tree,
    Water,
    Blocking
}

/// <summary>
/// The rules of the expedition as the agent found them: the grid, the consumption table, which
/// modes survive water and how large the budget is. Nothing here is hard-coded — the model registers
/// what the archive told it and the code only checks the shape, so a rule the agent never looked up
/// surfaces as a planning failure instead of a quietly wrong assumption.
/// </summary>
public sealed class WorldModel
{
    private readonly Dictionary<string, VehicleProfile> _vehicles;
    private readonly HashSet<char> _blocking;
    private readonly HashSet<char> _water;
    private readonly HashSet<char> _tree;

    private WorldModel(TerrainMap map, IReadOnlyList<VehicleProfile> vehicles, string walkMode, double fuelBudget, double foodBudget,
        double treeExtraFuel, HashSet<char> blocking, HashSet<char> water, HashSet<char> tree, bool dismountAllowed)
    {
        Map = map;
        Vehicles = vehicles;
        WalkMode = walkMode;
        FuelBudget = fuelBudget;
        FoodBudget = foodBudget;
        TreeExtraFuel = treeExtraFuel;
        DismountAllowed = dismountAllowed;
        _vehicles = vehicles.ToDictionary(vehicle => vehicle.Name, StringComparer.OrdinalIgnoreCase);
        _blocking = blocking;
        _water = water;
        _tree = tree;
    }

    public TerrainMap Map { get; }

    public IReadOnlyList<VehicleProfile> Vehicles { get; }

    public string WalkMode { get; }

    public double FuelBudget { get; }

    public double FoodBudget { get; }

    public double TreeExtraFuel { get; }

    public bool DismountAllowed { get; }

    public VehicleProfile Walk => _vehicles[WalkMode];

    public VehicleProfile? Find(string name) => _vehicles.GetValueOrDefault(name.Trim());

    public TerrainKind Classify(char marker)
    {
        if (_blocking.Contains(marker))
            return TerrainKind.Blocking;
        if (_water.Contains(marker))
            return TerrainKind.Water;
        if (_tree.Contains(marker))
            return TerrainKind.Tree;
        return TerrainKind.Open;
    }

    /// <summary>The single place that decides whether a move is survivable; planner and simulator share it.</summary>
    public bool CanEnter(VehicleProfile mode, Position target, out string refusal)
    {
        if (!Map.Contains(target))
        {
            refusal = $"{target} is outside the map.";
            return false;
        }

        var kind = Classify(Map.At(target));
        if (kind == TerrainKind.Blocking)
        {
            refusal = $"{target} is marked {Map.At(target)} and blocks movement completely.";
            return false;
        }

        if (kind == TerrainKind.Water && !mode.CanEnterWater)
        {
            refusal = $"{target} is water and {mode.Name} cannot enter it.";
            return false;
        }

        refusal = string.Empty;
        return true;
    }

    public (double Fuel, double Food) CostOf(VehicleProfile mode, Position target)
    {
        var extra = mode.IsPowered && Classify(Map.At(target)) == TerrainKind.Tree ? TreeExtraFuel : 0;
        return (mode.FuelPerMove + extra, mode.FoodPerMove);
    }

    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Map.Render());
        sb.AppendLine($"budget: fuel {Format(FuelBudget)}, food {Format(FoodBudget)}; tree tiles add {Format(TreeExtraFuel)} fuel for powered modes");
        sb.AppendLine("mode      fuel/move  food/move  water  selectable at start");
        foreach (var vehicle in Vehicles)
            sb.AppendLine($"{vehicle.Name,-9} {Format(vehicle.FuelPerMove),9}  {Format(vehicle.FoodPerMove),9}  {(vehicle.CanEnterWater ? "yes" : "no"),-5}  {(vehicle.SelectableAtStart ? "yes" : "no")}");
        sb.Append($"dismount to {WalkMode}: {(DismountAllowed ? "allowed" : "not allowed")}");
        return sb.ToString();
    }

    public static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // -------------------------------------------------------------------------
    // Registration
    // -------------------------------------------------------------------------

    public static bool TryParse(JsonElement root, out WorldModel? world, out string error)
    {
        world = null;
        var problems = new List<string>();

        var rows = ReadMapRows(root, problems);
        var startMarker = ReadMarker(root, "start_marker", 'S', problems);
        var goalMarker = ReadMarker(root, "goal_marker", 'G', problems);
        var blocking = ReadMarkerSet(root, "blocking_markers", problems);
        var water = ReadMarkerSet(root, "water_markers", problems);
        var tree = ReadMarkerSet(root, "tree_markers", problems);
        var open = ReadMarkerSet(root, "open_markers", problems);
        if (open.Count == 0)
            open.Add('.');
        var fuelBudget = ReadNumber(root, "fuel_budget", problems);
        var foodBudget = ReadNumber(root, "food_budget", problems);

        if (fuelBudget <= 0 && foodBudget <= 0)
            problems.Add("Both budgets are zero, so no journey is possible. Register the supplies the courier actually leaves with.");
        var treeExtraFuel = ReadOptionalNumber(root, "tree_extra_fuel") ?? 0;
        var walkMode = root.TryGetProperty("walk_mode", out var walkEl) && walkEl.ValueKind == JsonValueKind.String
            ? walkEl.GetString()!.Trim()
            : "walk";
        var dismountAllowed = !root.TryGetProperty("dismount_allowed", out var dismountEl) || dismountEl.ValueKind != JsonValueKind.False;
        var vehicles = ReadVehicles(root, problems);

        TerrainMap? map = null;
        if (rows.Count > 0 && !TerrainMap.TryCreate(rows, startMarker, goalMarker, out map, out var mapError))
            problems.Add(mapError!);

        // A marker nobody classified would silently read as open ground, which is the one mistake the
        // simulator cannot catch later: the route would look survivable and die on the real terrain.
        if (map is not null)
        {
            var classified = new HashSet<char>(open) { startMarker, goalMarker };
            classified.UnionWith(blocking);
            classified.UnionWith(water);
            classified.UnionWith(tree);

            var unknown = map.RawRows.SelectMany(row => row).Where(marker => !classified.Contains(marker)).Distinct().ToList();
            if (unknown.Count > 0)
                problems.Add($"The map uses markers you did not classify: {string.Join(" ", unknown.Select(marker => $"'{marker}'"))}. Say for each whether it blocks movement, counts as water, as trees or as open ground.");
        }

        if (vehicles.Count > 0 && vehicles.All(vehicle => !string.Equals(vehicle.Name, walkMode, StringComparison.OrdinalIgnoreCase)))
            problems.Add($"walk_mode {walkMode} is not among the registered vehicles.");

        if (problems.Count > 0)
        {
            error = string.Join(" ", problems);
            return false;
        }

        world = new WorldModel(map!, vehicles, walkMode, fuelBudget, foodBudget, treeExtraFuel, blocking, water, tree, dismountAllowed);
        error = string.Empty;
        return true;
    }

    private static List<string> ReadMapRows(JsonElement root, List<string> problems)
    {
        if (!root.TryGetProperty("map", out var mapEl))
        {
            problems.Add("Field map is missing.");
            return [];
        }

        if (mapEl.ValueKind == JsonValueKind.String)
            return [.. mapEl.GetString()!.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)];

        if (mapEl.ValueKind == JsonValueKind.Array)
        {
            var rows = new List<string>();
            foreach (var row in mapEl.EnumerateArray())
            {
                if (row.ValueKind == JsonValueKind.String)
                    rows.Add(row.GetString()!);
                else if (row.ValueKind == JsonValueKind.Array)
                    rows.Add(string.Concat(row.EnumerateArray().Select(cell => cell.ToString())));
            }
            return rows;
        }

        problems.Add("Field map must be an array of row strings, an array of row arrays, or one string with line breaks.");
        return [];
    }

    private static char ReadMarker(JsonElement root, string name, char fallback, List<string> problems)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return fallback;

        var text = element.GetString()!;
        if (text.Length != 1)
        {
            problems.Add($"Field {name} must be a single character.");
            return fallback;
        }

        return text[0];
    }

    private static HashSet<char> ReadMarkerSet(JsonElement root, string name, List<string> problems)
    {
        var markers = new HashSet<char>();
        if (!root.TryGetProperty(name, out var element))
            return markers;

        var texts = element.ValueKind switch
        {
            JsonValueKind.String => [element.GetString()!],
            JsonValueKind.Array => element.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList(),
            _ => new List<string>()
        };

        foreach (var text in texts)
        {
            if (text.Length != 1)
            {
                problems.Add($"Field {name} takes single-character markers, got {text}.");
                continue;
            }

            markers.Add(text[0]);
        }

        return markers;
    }

    private static double ReadNumber(JsonElement root, string name, List<string> problems)
    {
        var value = ReadOptionalNumber(root, name);
        if (value is null)
        {
            problems.Add($"Field {name} is missing or not a number.");
            return 0;
        }

        return value.Value;
    }

    private static double? ReadOptionalNumber(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element))
            return null;

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.String when double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static List<VehicleProfile> ReadVehicles(JsonElement root, List<string> problems)
    {
        var vehicles = new List<VehicleProfile>();
        if (!root.TryGetProperty("vehicles", out var listEl) || listEl.ValueKind != JsonValueKind.Array || listEl.GetArrayLength() == 0)
        {
            problems.Add("Field vehicles must be a non-empty array.");
            return vehicles;
        }

        foreach (var element in listEl.EnumerateArray())
        {
            var name = element.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String ? nameEl.GetString()!.Trim() : string.Empty;
            if (name.Length == 0)
            {
                problems.Add("Every vehicle needs a name.");
                continue;
            }

            var fuel = ReadOptionalNumber(element, "fuel_per_move");
            var food = ReadOptionalNumber(element, "food_per_move");
            if (fuel is null || food is null)
            {
                problems.Add($"Vehicle {name} needs numeric fuel_per_move and food_per_move.");
                continue;
            }

            if (fuel < 0 || food < 0)
            {
                problems.Add($"Vehicle {name} has negative consumption.");
                continue;
            }

            if (vehicles.Any(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"Vehicle {name} is registered twice.");
                continue;
            }

            var water = element.TryGetProperty("can_enter_water", out var waterEl) && waterEl.ValueKind == JsonValueKind.True;
            var selectable = !element.TryGetProperty("selectable_at_start", out var selectableEl) || selectableEl.ValueKind != JsonValueKind.False;
            vehicles.Add(new VehicleProfile(name, fuel.Value, food.Value, water, selectable));
        }

        return vehicles;
    }
}
