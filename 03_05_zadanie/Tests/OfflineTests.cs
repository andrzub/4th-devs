using System.Text.Json;
using _03_05_zadanie.Hub;
using _03_05_zadanie.Mission;
using _03_05_zadanie.World;

namespace _03_05_zadanie.Tests;

/// <summary>
/// Everything that decides whether a route is worth sending, checked offline against hand-built
/// maps. The hub answers a bad route with one rejection and a spent attempt, so the rules that judge
/// a route are verified here — no network, no API key, no submission burned. The maps are synthetic
/// on purpose: the code knows how to reason about a world, never what this particular world contains.
/// </summary>
public static class OfflineTests
{
    public static bool Run()
    {
        var results = new List<(string Case, bool Passed, string Detail)>();

        void Expect(string name, bool passed, string detail = "") => results.Add((name, passed, detail));

        void ExpectRejected(string name, string json, string expectedFragment)
        {
            var parsed = WorldModel.TryParse(Json(json), out _, out var error);
            Expect(name, !parsed && error.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase), error);
        }

        void ExpectRoute(string name, WorldModel world, string route, bool valid)
        {
            var outcome = RouteSimulator.Run(world, RouteInstructions.Split([route]));
            Expect(name, outcome.IsValid == valid, outcome.Failure ?? outcome.Report);
        }

        // --- registration: the shape is checked, so a malformed world never reaches the planner ----
        ExpectRejected("a map with rows of different length is rejected", Registration(["S..", "...."]), "rectangular");
        ExpectRejected("a map without a goal is rejected", Registration(["S..", "..."]), "no 'G' tile");
        ExpectRejected("a map with two starts is rejected", Registration(["S.S", "..G"]), "2 'S' tiles");
        ExpectRejected("a vehicle with negative consumption is rejected",
            Registration(["S.G"], vehicles: """[{"name":"walk","fuel_per_move":0,"food_per_move":-1}]"""), "negative");
        ExpectRejected("a walk mode missing from the table is rejected",
            Registration(["S.G"], vehicles: """[{"name":"cart","fuel_per_move":1,"food_per_move":1}]"""), "walk_mode");
        ExpectRejected("a world without budgets is rejected", """{"map":["S.G"],"vehicles":[{"name":"walk","fuel_per_move":0,"food_per_move":1}]}""", "fuel_budget");
        ExpectRejected("a world with nothing to travel on is rejected", Registration(["S.G"], fuel: 0, food: 0), "no journey is possible");
        ExpectRejected("a map marker nobody classified is rejected", Registration(["S G"]), "did not classify");

        var fromOneString = WorldModel.TryParse(Json(Registration([], mapLiteral: "\"S..\\n..G\"")), out var stringMap, out var stringError);
        Expect("a map given as one string with line breaks is accepted", fromOneString && stringMap!.Map.Goal == new Position(2, 3), stringError);

        var customOpen = WorldModel.TryParse(Json("""
            {"map":["S__G"],"open_markers":["_"],"fuel_budget":10,"food_budget":10,
             "vehicles":[{"name":"walk","fuel_per_move":0,"food_per_move":1}]}
            """), out _, out var openError);
        Expect("a world may declare its own open-ground marker", customOpen, openError);

        // --- the rules themselves ----------------------------------------------------------------
        var crossing = Build(["S.W.G", "RRRRR"]);
        Expect("a blocking marker refuses every mode", !crossing.CanEnter(crossing.Find("skiff")!, new Position(2, 1), out _));
        Expect("water refuses a mode that cannot swim", !crossing.CanEnter(crossing.Find("cart")!, new Position(1, 3), out _));
        Expect("water accepts a mode that can", crossing.CanEnter(crossing.Find("walk")!, new Position(1, 3), out _));

        var wooded = Build(["STG"]);
        Expect("a tree costs a powered mode extra fuel", Math.Abs(wooded.CostOf(wooded.Find("skiff")!, new Position(1, 2)).Fuel - 1.2) < 1e-9);
        Expect("a tree costs an unpowered mode nothing extra", Math.Abs(wooded.CostOf(wooded.Find("walk")!, new Position(1, 2)).Fuel) < 1e-9);

        // --- the guard in front of every submission ----------------------------------------------
        var open = Build(["S..", "...", "..G"]);
        ExpectRoute("a route that reaches the goal is accepted", open, "walk right right down down", true);
        ExpectRoute("a route that stops short is refused", open, "walk right right down", false);
        ExpectRoute("a route that walks off the map is refused", open, "walk up", false);
        ExpectRoute("a route that continues past the goal is refused", open, "walk right right down down left", false);
        ExpectRoute("an unknown step is refused", open, "walk right forward down down", false);
        ExpectRoute("a first entry that is not a mode is refused", open, "right right down down", false);
        ExpectRoute("dismounting while already on foot is refused", open, "walk dismount right right down down", false);
        ExpectRoute("a land mode driven into water is refused", crossing, "cart right right", false);
        ExpectRoute("dismounting before the water saves the same route", crossing, "cart right dismount right right right", true);
        ExpectRoute("a route that runs out of food is refused", Build(["S........G"], food: 6), "walk right right right right right right right right right", false);
        ExpectRoute("a mode that cannot be chosen at departure is refused",
            Build(["S.G"], vehicles: """[{"name":"walk","fuel_per_move":0,"food_per_move":2},{"name":"tram","fuel_per_move":0.1,"food_per_move":0.1,"selectable_at_start":false}]"""),
            "tram right right", false);

        // --- planning ----------------------------------------------------------------------------
        var ferryPlan = RoutePlanner.Plan(Build(["S.WG"]));
        Expect("the cheapest departure wins over the merely possible", ferryPlan.Best?.Departure == "skiff", ferryPlan.Render());
        Expect("the plan leaves the vehicle when it cannot cross the water",
            ferryPlan.Best?.Instructions.Contains(RouteInstructions.Dismount) == true, ferryPlan.Render());

        var corridor = RoutePlanner.Plan(Build(["S........G"]));
        Expect("a mode that only fails on the budget is reported as over budget",
            corridor.Options.Single(option => option.Departure == "walk").Note.Contains("over budget"), corridor.Render());
        Expect("the long corridor is still crossed by a cheaper mode", corridor.Best is not null, corridor.Render());

        var walled = RoutePlanner.Plan(Build(["S.RG", "..R.", "..R."]));
        Expect("a walled-off goal leaves no route at all", walled.Best is null, walled.Render());
        Expect("and every mode says why", walled.Options.All(option => option.Note.Contains("cannot reach")), walled.Render());

        // A plan the simulator would reject is worse than no plan, so the two halves are checked
        // against each other rather than each on its own.
        foreach (var map in new[]
                 {
                     new[] { "S..", "...", "..G" },
                     ["S.WG"],
                     ["S.W.G", "RRRRR"],
                     ["STTTG"],
                     ["S........G"],
                     new[] { "S.T.W..", ".RR.WR.", "..R...G" }
                 })
        {
            var plan = RoutePlanner.Plan(Build(map));
            foreach (var option in plan.Options.Where(option => option.Route is not null))
            {
                var outcome = RouteSimulator.Run(Build(map), option.Route!.Instructions);
                Expect($"planned {option.Departure} route on {string.Join("/", map)} survives the guard", outcome.IsValid, outcome.Failure ?? string.Empty);
                Expect($"planned {option.Departure} route on {string.Join("/", map)} is costed correctly",
                    Math.Abs(outcome.FuelUsed - option.Route.Fuel) < 1e-9 && Math.Abs(outcome.FoodUsed - option.Route.Food) < 1e-9,
                    $"plan says {WorldModel.Format(option.Route.Fuel)}/{WorldModel.Format(option.Route.Food)}, replay says {WorldModel.Format(outcome.FuelUsed)}/{WorldModel.Format(outcome.FoodUsed)}");
            }
        }

        // --- what the run knows about the toolbox and about itself -------------------------------
        var registry = new ToolRegistry();
        Expect("tools are learned from a search answer",
            registry.Absorb("""{"code":210,"tools":[{"name":"atlas","url":"/api/atlas","description":"maps"},{"name":"ledger","url":"/api/ledger"}]}""") == 2);
        Expect("the same tool is not learned twice", registry.Absorb("""{"tools":[{"name":"atlas","url":"/api/atlas"}]}""") == 0);
        Expect("a malformed answer teaches nothing", registry.Absorb("not json at all") == 0);
        Expect("a known tool resolves by name", registry.TryResolve("atlas", out _));
        Expect("a known tool resolves by address", registry.TryResolve("/api/ledger", out _));
        Expect("an invented tool does not resolve", !registry.TryResolve("oracle", out _));

        var mission = new MissionState();
        mission.ScanForFlag("""{"code":0,"message":"Route accepted {{FLG:EXAMPLE}} well done"}""");
        Expect("the flag is read out of the raw answer", mission.Flag == "{{FLG:EXAMPLE}}", mission.Flag ?? "none");
        mission.ScanForFlag("{{FLG:SOMETHING_ELSE}}");
        Expect("a second flag never overwrites the first", mission.Flag == "{{FLG:EXAMPLE}}", mission.Flag ?? "none");

        Expect("a preview with no state renders its message",
            PreviewReport.Render("""{"code":-980,"message":"No preview state found for this API key."}""").Contains("No preview state"));

        return Report(results);
    }

    private const string DefaultVehicles = """
        [{"name":"walk","fuel_per_move":0,"food_per_move":2,"can_enter_water":true},
         {"name":"cart","fuel_per_move":0.5,"food_per_move":1},
         {"name":"skiff","fuel_per_move":1,"food_per_move":0.1,"can_enter_water":true}]
        """;

    private static string Registration(IReadOnlyList<string> map, double fuel = 10, double food = 10, string vehicles = DefaultVehicles, string? mapLiteral = null)
    {
        var rows = mapLiteral ?? "[" + string.Join(",", map.Select(row => $"\"{row}\"")) + "]";
        return $$"""
            {"map":{{rows}},"blocking_markers":["R"],"water_markers":["W"],"tree_markers":["T"],"tree_extra_fuel":0.2,
             "fuel_budget":{{WorldModel.Format(fuel)}},"food_budget":{{WorldModel.Format(food)}},"vehicles":{{vehicles}}}
            """;
    }

    private static WorldModel Build(IReadOnlyList<string> map, double fuel = 10, double food = 10, string vehicles = DefaultVehicles)
    {
        if (!WorldModel.TryParse(Json(Registration(map, fuel, food, vehicles)), out var world, out var error))
            throw new InvalidOperationException($"Test world is malformed: {error}");

        return world!;
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static bool Report(List<(string Case, bool Passed, string Detail)> results)
    {
        foreach (var (name, passed, detail) in results)
        {
            Console.WriteLine($"  {(passed ? "ok  " : "FAIL")}  {name}");
            if (!passed && detail.Length > 0)
                Console.WriteLine($"          {detail}");
        }

        var failed = results.Count(result => !result.Passed);
        Console.WriteLine();
        Console.WriteLine(failed == 0 ? $"All {results.Count} cases passed." : $"{failed} of {results.Count} cases FAILED.");
        return failed == 0;
    }
}
