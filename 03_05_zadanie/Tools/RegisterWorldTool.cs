using System.Text.Json;
using _03_05_zadanie.Mission;

namespace _03_05_zadanie.Tools;

/// <summary>
/// Where what the agent read turns into something the code can compute with. The shape is validated
/// and the result is echoed back as a numbered grid and a consumption table, so a misread rule is
/// visible before it becomes a route.
/// </summary>
public sealed class RegisterWorldTool(Expedition expedition) : ITool
{
    public string Name => "register_world";

    public string Description =>
        "Writes down the rules you have found so that the route can be computed from them: the terrain grid, which "
        + "markers block movement, count as water or as trees, how much fuel and food each travel mode burns per "
        + "move, which modes survive water, and how large the starting supplies are. Every marker on the map has to "
        + "be classified and the supplies have to be real. Registering again replaces the previous version. Returns "
        + "the world as the planner now understands it.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {"type":"object","properties":{
          "map":{"type":"array","items":{"type":"string"},"description":"Grid rows, one string per row, north first."},
          "start_marker":{"type":"string","description":"Marker of the starting tile. Default S."},
          "goal_marker":{"type":"string","description":"Marker of the destination tile. Default G."},
          "blocking_markers":{"type":"array","items":{"type":"string"},"description":"Markers no mode can enter."},
          "water_markers":{"type":"array","items":{"type":"string"},"description":"Markers that count as water."},
          "tree_markers":{"type":"array","items":{"type":"string"},"description":"Markers that count as trees."},
          "open_markers":{"type":"array","items":{"type":"string"},"description":"Markers that are plain open ground. Default the dot."},
          "tree_extra_fuel":{"type":"number","description":"Extra fuel a powered mode burns entering a tree tile."},
          "fuel_budget":{"type":"number","description":"Fuel available at departure."},
          "food_budget":{"type":"number","description":"Food available at departure."},
          "walk_mode":{"type":"string","description":"Name of the on-foot mode. Default walk."},
          "dismount_allowed":{"type":"boolean","description":"Whether the traveller may leave the vehicle mid-route."},
          "vehicles":{"type":"array","description":"Every travel mode, including the on-foot one.","items":{"type":"object","properties":{
            "name":{"type":"string"},
            "fuel_per_move":{"type":"number"},
            "food_per_move":{"type":"number"},
            "can_enter_water":{"type":"boolean"},
            "selectable_at_start":{"type":"boolean"}},
            "required":["name","fuel_per_move","food_per_move"]}}},
         "required":["map","fuel_budget","food_budget","vehicles"]}
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        }
        catch (JsonException ex)
        {
            return Task.FromResult($"The arguments are not valid JSON: {ex.Message}");
        }

        using (document)
            return Task.FromResult(expedition.RegisterWorld(document.RootElement));
    }
}
