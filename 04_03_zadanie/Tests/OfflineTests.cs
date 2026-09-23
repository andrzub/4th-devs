using System.Text.Json.Nodes;
using _04_03_zadanie.City;
using _04_03_zadanie.Mission;

namespace _04_03_zadanie.Tests;

/// <summary>
/// Everything that decides where units go and what they may do, checked without a network or a key.
/// Every point spent on the hub is gone for good and a wrong action can strand a scout, so the routes,
/// the plan and the guard are settled here, against the map and the tariff exactly as the hub served them.
/// </summary>
public static class OfflineTests
{
    /// <summary>The getMap reply as the hub served it.</summary>
    private const string MapReply = """
        {
          "code": 80,
          "message": "Map loaded.",
          "map": {
            "name": "Domatowo",
            "size": 11,
            "tiles": {
              "road": { "label": "Ulica", "symbol": "UL" },
              "tree": { "label": "Drzewa", "symbol": "DR" },
              "house": { "label": "Dom", "symbol": "DM" },
              "empty": { "label": "Pusta przestrzen", "symbol": "  " },
              "block1": { "label": "Blok 1p", "symbol": "B1" },
              "block2": { "label": "Blok 2p", "symbol": "B2" },
              "block3": { "label": "Blok 3p", "symbol": "B3" },
              "church": { "label": "Kosciol", "symbol": "KS" },
              "school": { "label": "Szkola", "symbol": "SZ" },
              "parking": { "label": "Parking", "symbol": "PK" },
              "field": { "label": "Boisko", "symbol": "BS" }
            },
            "grid": [
              ["tree","road","road","road","empty","block3","block3","tree","empty","parking","parking"],
              ["tree","tree","empty","road","road","block3","block3","tree","road","parking","parking"],
              ["empty","empty","empty","road","parking","empty","empty","tree","road","empty","empty"],
              ["block1","block1","empty","road","parking","school","school","school","road","field","field"],
              ["block1","block1","empty","road","parking","school","school","school","road","field","field"],
              ["road","road","road","road","road","road","road","road","road","road","empty"],
              ["block2","block2","empty","road","empty","church","church","church","empty","tree","empty"],
              ["block2","block2","empty","road","empty","church","church","church","empty","tree","empty"],
              ["empty","road","road","road","road","road","road","road","road","road","empty"],
              ["block3","block3","block3","empty","tree","empty","empty","block3","block3","tree","empty"],
              ["block3","block3","block3","empty","tree","empty","empty","block3","block3","tree","empty"]
            ]
          }
        }
        """;

    /// <summary>The actionCost reply as the hub served it.</summary>
    private const string CostReply = """
        {
          "code": 67,
          "message": "Action costs loaded.",
          "costs": [
            { "action": "create", "variant": "scout", "cost": 5 },
            { "action": "create", "variant": "transporter", "base_cost": 5, "cost_per_passenger": 5, "formula": "5 + (passengers * 5)" },
            { "action": "move", "variant": "scout", "cost_per_field": 7, "formula": "steps * 7" },
            { "action": "move", "variant": "transporter", "cost_per_field": 1, "formula": "steps * 1" },
            { "action": "inspect", "cost": 1 },
            { "action": "dismount", "cost": 0 },
            { "action": "reset", "cost": 0 },
            { "action": "getLogs", "cost": 0 },
            { "action": "getObjects", "cost": 0 },
            { "action": "getMap", "cost": 0 },
            { "action": "searchSymbol", "cost": 0 },
            { "action": "expenses", "cost": 0 },
            { "action": "actionCost", "cost": 0 },
            { "action": "callHelicopter", "cost": 0 },
            { "action": "help", "cost": 0 }
          ]
        }
        """;

    /// <summary>getObjects as the hub served it after the first leg: note the "typ" spelling.</summary>
    private const string ObjectsReply = """
        {
          "code": 70,
          "message": "Objects list loaded.",
          "objects": [
            { "typ": "transporter", "position": "C9", "id": "9d4c0ed78c955907b9a2b94a5f02007c" },
            { "typ": "scout", "position": "C8", "id": "40cab23ecd9a5cfc5d1ffc840ebd1244" }
          ]
        }
        """;

    private const string LogsReply = """
        {
          "code": 60,
          "message": "Inspect logs loaded.",
          "logs": [
            { "scout": "40cab23ecd9a5cfc5d1ffc840ebd1244", "msg": "Nie ma tu człowieka. Tylko kable, puszki i stary plecak.", "field": "C8" }
          ]
        }
        """;

    private const string PullReply = """
        {
          "code": 10,
          "stats": { "used_transporters": 1, "used_scouts": 3, "action_points_used": 28, "action_points_left": 272, "human_found": true, "human_found_at": "f2", "mission_flag": "" },
          "objects": [
            { "id": "0123456789abcdef0123456789abcdef", "type": "transporter", "position": "E2" },
            { "id": "fedcba9876543210fedcba9876543210", "type": "scout", "position": "F2" }
          ],
          "tasks": []
        }
        """;

    public static bool Run()
    {
        var failures = 0;
        var total = 0;

        void Check(string name, Action assertion)
        {
            total++;
            try
            {
                assertion();
                Console.WriteLine($"  pass  {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"  FAIL  {name}: {exception.Message}");
            }
        }

        var map = CityMap.Parse(MapReply);
        var costs = CostTable.Parse(CostReply);
        var clusters = ClusterFinder.Find(map, "B3");

        // -- coordinates ----------------------------------------------------------------------

        Check("coordinates parse in the hub's notation, any case, and print back the same", () =>
        {
            Expect(Coordinate.Parse("A1") == new Coordinate(1, 1), "A1");
            Expect(Coordinate.Parse("K11") == new Coordinate(11, 11), "K11");
            Expect(Coordinate.Parse(" f6 ") == new Coordinate(6, 6), "f6");
            Expect(Coordinate.Parse("F6").ToString() == "F6", "print");
        });

        Check("coordinates off the board are refused", () =>
        {
            foreach (var text in new[] { "L1", "A12", "A0", "AA", "6F", "", "F" })
                Expect(!Coordinate.TryParse(text, out _), $"'{text}' accepted");
        });

        Check("neighbours stay on the board and distance is Manhattan", () =>
        {
            Expect(Coordinate.Parse("A1").Neighbours().Count() == 2, "corner has two neighbours");
            Expect(Coordinate.Parse("F6").Neighbours().Count() == 4, "middle has four");
            Expect(Coordinate.Parse("A6").DistanceTo(Coordinate.Parse("E2")) == 8, "A6-E2");
        });

        // -- the map --------------------------------------------------------------------------

        Check("the map parses with the legend and the grid the hub sent", () =>
        {
            Expect(map.Size == 11, $"size {map.Size}");
            Expect(map.Legend.Count == 11, $"legend {map.Legend.Count}");
            Expect(map[Coordinate.Parse("F1")].Symbol == "B3", "F1 is B3");
            Expect(map[Coordinate.Parse("E1")].Kind.Name == "empty", "E1 is empty");
            Expect(map.IsRoad(Coordinate.Parse("A6")), "A6 is a street");
            Expect(!map.IsRoad(Coordinate.Parse("K6")), "K6 is not a street");
        });

        Check("fields are found by symbol: 14 tallest blocks, 33 street fields", () =>
        {
            Expect(map.FieldsWithSymbol("B3").Count == 14, $"B3 {map.FieldsWithSymbol("B3").Count}");
            Expect(map.FieldsWithSymbol("UL").Count == 33, $"UL {map.FieldsWithSymbol("UL").Count}");
            Expect(map.KindOfSymbol("B3")?.Label == "Blok 3p", "label");
        });

        Check("the map renders like the preview, blank symbols as dots", () =>
        {
            var lines = map.Render().Split(Environment.NewLine);
            Expect(lines.Length == 12, $"{lines.Length} lines");
            Expect(lines[1].Trim() == "1  DR UL UL UL .. B3 B3 DR .. PK PK", $"row 1: '{lines[1]}'");
            Expect(lines[6].Trim() == "6  UL UL UL UL UL UL UL UL UL UL ..", $"row 6: '{lines[6]}'");
        });

        Check("a map of another size or with an unknown tile kind is refused", () =>
        {
            ExpectThrows<FormatException>(() => CityMap.Parse("""{"map":{"size":3,"tiles":{},"grid":[[],[],[]]}}"""), "size 3");
            ExpectThrows<FormatException>(() => CityMap.Parse(MapReply.Replace("\"tree\",\"road\",\"road\",\"road\",\"empty\"", "\"lava\",\"road\",\"road\",\"road\",\"empty\"")), "unknown kind");
        });

        // -- routes ---------------------------------------------------------------------------

        Check("transporter routes follow streets: A6-E2 is 8 fields, A6-C9 is 7, A6-H9 is 10", () =>
        {
            Expect(Pathfinder.RoadPath(map, Coordinate.Parse("A6"), Coordinate.Parse("E2"))!.Count == 8, "A6-E2");
            Expect(Pathfinder.RoadPath(map, Coordinate.Parse("A6"), Coordinate.Parse("C9"))!.Count == 7, "A6-C9");
            Expect(Pathfinder.RoadPath(map, Coordinate.Parse("A6"), Coordinate.Parse("H9"))!.Count == 10, "A6-H9");
            Expect(Pathfinder.RoadPath(map, Coordinate.Parse("C9"), Coordinate.Parse("H9"))!.Count == 5, "C9-H9");
        });

        Check("a transporter route lists every field after the start and ends at the target", () =>
        {
            var route = Pathfinder.RoadPath(map, Coordinate.Parse("A6"), Coordinate.Parse("E2"))!;
            Expect(route[0] == Coordinate.Parse("B6"), $"first {route[0]}");
            Expect(route[^1] == Coordinate.Parse("E2"), $"last {route[^1]}");
            Expect(route.All(map.IsRoad), "every field is a street");
        });

        Check("a transporter cannot reach a block or an isolated field, and staying put is an empty route", () =>
        {
            Expect(Pathfinder.RoadPath(map, Coordinate.Parse("A6"), Coordinate.Parse("F1")) is null, "F1 is a block");
            Expect(Pathfinder.RoadPath(map, Coordinate.Parse("A6"), Coordinate.Parse("K6")) is null, "K6 is empty ground");
            Expect(Pathfinder.RoadPath(map, Coordinate.Parse("D6"), Coordinate.Parse("D6"))!.Count == 0, "D6-D6");
        });

        Check("scout routes are Manhattan distances over anything", () =>
        {
            Expect(Pathfinder.ScoutPath(Coordinate.Parse("E2"), Coordinate.Parse("F2")).Count == 1, "E2-F2");
            Expect(Pathfinder.ScoutPath(Coordinate.Parse("B10"), Coordinate.Parse("C11")).Count == 2, "B10-C11");
            Expect(Pathfinder.ScoutPath(Coordinate.Parse("A6"), Coordinate.Parse("F1")).Count == 10, "A6-F1 through blocks");
        });

        // -- clusters -------------------------------------------------------------------------

        Check("the tallest blocks form three clusters of 4, 6 and 4 fields", () =>
        {
            Expect(clusters.Count == 3, $"{clusters.Count} clusters");
            Expect(clusters.Select(cluster => cluster.Fields.Count).SequenceEqual([4, 6, 4]), string.Join(",", clusters.Select(cluster => cluster.Fields.Count)));
            Expect(clusters.Select(cluster => cluster.Name).SequenceEqual(["B3 F1-G2", "B3 A10-C11", "B3 H10-I11"]), string.Join(" | ", clusters.Select(cluster => cluster.Name)));
        });

        Check("each cluster's stops are the streets touching it", () =>
        {
            Expect(clusters[0].Stops.Select(stop => stop.ToString()).SequenceEqual(["E2"]), $"top: {string.Join(",", clusters[0].Stops)}");
            Expect(clusters[1].Stops.Select(stop => stop.ToString()).SequenceEqual(["B9", "C9"]), $"bottom-left: {string.Join(",", clusters[1].Stops)}");
            Expect(clusters[2].Stops.Select(stop => stop.ToString()).SequenceEqual(["H9", "I9"]), $"bottom-right: {string.Join(",", clusters[2].Stops)}");
        });

        Check("the field a scout lands on from a stop is the block right next to it", () =>
        {
            Expect(clusters[0].FieldsTouching(Coordinate.Parse("E2")).Select(field => field.ToString()).SequenceEqual(["F2"]), "E2 -> F2");
            Expect(clusters[1].FieldsTouching(Coordinate.Parse("C9")).Select(field => field.ToString()).SequenceEqual(["C10"]), "C9 -> C10");
            Expect(clusters[2].FieldsTouching(Coordinate.Parse("I9")).Select(field => field.ToString()).SequenceEqual(["I10"]), "I9 -> I10");
        });

        Check("a symbol nobody placed on the board yields no clusters", () =>
            Expect(ClusterFinder.Find(map, "DM").Count == 0, "house clusters"));

        // -- the tariff -----------------------------------------------------------------------

        Check("actionCost parses into the task's own prices", () =>
        {
            Expect(costs == CostTable.FromTaskText, $"parsed {costs}");
            Expect(costs.Transporter(3) == 20, $"transporter(3) = {costs.Transporter(3)}");
            Expect(costs.ScoutMove(5) == 35, "5 scout fields");
        });

        Check("a tariff missing a price is refused rather than guessed", () =>
            ExpectThrows<FormatException>(() => CostTable.Parse("""{"costs":[{"action":"inspect","cost":1}]}"""), "missing"));

        // -- sweeps ---------------------------------------------------------------------------

        Check("one scout sweeps the six-field cluster in five steps from either landing", () =>
        {
            var fromB10 = SearchPlanner.SweepFrom(Coordinate.Parse("B10"), clusters[1].Fields, costs);
            var fromC10 = SearchPlanner.SweepFrom(Coordinate.Parse("C10"), clusters[1].Fields, costs);
            Expect(fromB10.Count == 6 && fromB10.Sum(step => step.Walk) == 5, $"from B10: {fromB10.Sum(step => step.Walk)} walks");
            Expect(fromC10.Count == 6 && fromC10.Sum(step => step.Walk) == 5, $"from C10: {fromC10.Sum(step => step.Walk)} walks");
            Expect(fromB10[0] == new SweepStep(Coordinate.Parse("B10"), 0), "starts by inspecting the landing field");
            Expect(fromB10.Select(step => step.Field).Distinct().Count() == 6, "every field once");
        });

        Check("a scout that landed beside the cluster walks in first", () =>
        {
            var sweep = SearchPlanner.SweepFrom(Coordinate.Parse("E1"), clusters[0].Fields, costs);
            Expect(sweep.Count == 4, $"{sweep.Count} steps");
            Expect(sweep[0].Walk == 1 && sweep[0].Field == Coordinate.Parse("F1"), $"first step {sweep[0]}");
            Expect(sweep.Sum(step => step.Walk) == 4, $"{sweep.Sum(step => step.Walk)} walks");
        });

        Check("a sweep over the fields left after a hit is only those fields", () =>
        {
            var remaining = clusters[0].Fields.Where(field => field != Coordinate.Parse("F2")).ToList();
            var sweep = SearchPlanner.SweepFrom(Coordinate.Parse("F2"), remaining, costs);
            Expect(sweep.Count == 3 && sweep.Sum(step => step.Walk) == 3, $"{sweep.Count} steps, {sweep.Sum(step => step.Walk)} walks");
        });

        Check("a heavy weight pulls a field forward in the sweep, a light one does not", () =>
        {
            var plain = SearchPlanner.SweepFrom(Coordinate.Parse("F2"), clusters[0].Fields, costs);
            var light = SearchPlanner.SweepFrom(Coordinate.Parse("F2"), clusters[0].Fields, costs, Weighted("G1", 10.0));
            var heavy = SearchPlanner.SweepFrom(Coordinate.Parse("F2"), clusters[0].Fields, costs, Weighted("G1", 100.0));

            // Inspecting the landing field costs one point and two fields sit one step away, so G1 (two steps)
            // comes third; a tenfold weight does not overturn that, a hundredfold sends the scout straight to G1.
            Expect(IndexOf(plain, "G1") == 2, $"plain: {string.Join(" ", plain.Select(step => step.Field))}");
            Expect(IndexOf(light, "G1") == 2, $"light: {string.Join(" ", light.Select(step => step.Field))}");
            Expect(IndexOf(heavy, "G1") == 0, $"heavy: {string.Join(" ", heavy.Select(step => step.Field))}");

            Dictionary<Coordinate, double> Weighted(string field, double weight) =>
                clusters[0].Fields.ToDictionary(candidate => candidate, candidate => candidate == Coordinate.Parse(field) ? weight : 1.0);

            int IndexOf(IReadOnlyList<SweepStep> sweep, string field) =>
                sweep.Select((step, index) => (step, index)).First(pair => pair.step.Field == Coordinate.Parse(field)).index;
        });

        // -- the plan -------------------------------------------------------------------------

        var freshState = new OperationState([Coordinate.Parse("A6"), Coordinate.Parse("B6"), Coordinate.Parse("C6"), Coordinate.Parse("D6")]);
        var plans = SearchPlanner.PlanAll(map, clusters, costs, freshState);
        var plan = plans[0];

        Check("the plan visits every cluster once and inspects all 14 fields exactly once", () =>
        {
            Expect(plan.Visits.Count() == 3, $"{plan.Visits.Count()} visits");
            var inspected = plan.Visits.SelectMany(visit => visit.Sweep).Select(step => step.Field).ToList();
            Expect(inspected.Count == 14 && inspected.Distinct().Count() == 14, $"{inspected.Count} inspections, {inspected.Distinct().Count()} distinct");
        });

        Check("the plan fits the budget with room: worst case under 180 of 300, expected under 80", () =>
        {
            Expect(plan.FitsBudget, "fits");
            Expect(plan.WorstCase + plan.LandingReserve <= 180, $"worst {plan.WorstCase}+{plan.LandingReserve}");
            Expect(plan.Expected < 80, $"expected {plan.Expected:F1}");
            Expect(plan.Expected < plan.WorstCase, "expected below worst");
        });

        Check("every leg starts at a free spawn slot and every drive is a street route from where the transporter stands", () =>
        {
            foreach (var leg in plan.Legs)
            {
                Expect(freshState.SpawnSlots.Contains(leg.Spawn), $"spawn {leg.Spawn}");
                var position = leg.Spawn;
                foreach (var visit in leg.Visits)
                {
                    var route = Pathfinder.RoadPath(map, position, visit.Stop)!;
                    Expect(route.Count == visit.RoadSteps, $"{position}->{visit.Stop}: {visit.RoadSteps} vs {route.Count}");
                    Expect(visit.Cluster.Stops.Contains(visit.Stop) && visit.Cluster.Contains(visit.Landing), "stop and landing belong to the cluster");
                    position = visit.Stop;
                }
            }
        });

        Check("the plan's worst case is the sum of its own parts", () =>
        {
            var sum = 0;
            foreach (var leg in plan.Legs)
            {
                sum += costs.Transporter(leg.Passengers);
                foreach (var visit in leg.Visits)
                    sum += costs.TransporterMove(visit.RoadSteps) + costs.ScoutMove(visit.WalkSteps) + costs.Inspect * visit.Sweep.Count;
            }
            Expect(sum == plan.WorstCase, $"{sum} vs {plan.WorstCase}");
            Expect(plan.LandingReserve == costs.ScoutMove(SearchPlanner.LandingWalk) * 3, $"reserve {plan.LandingReserve}");
        });

        Check("all 24 orderings and splits are priced and the best is first", () =>
        {
            Expect(plans.Count == 24, $"{plans.Count} plans");
            Expect(plans.All(candidate => candidate.FitsBudget), "all fit 300");
            Expect(plans.Zip(plans.Skip(1)).All(pair => pair.First.Expected <= pair.Second.Expected), "sorted by expected");
        });

        Check("a budget too small for any full search is reported, not hidden", () =>
        {
            var poor = new OperationState(freshState.SpawnSlots) { Budget = 100 };
            var best = SearchPlanner.Plan(map, clusters, costs, poor);
            Expect(!best.FitsBudget, "should not fit");
            Expect(best.Summary.Contains("OVER BUDGET"), best.Summary);
        });

        Check("a plan from a board with a transporter parked on A6 starts from the next slot", () =>
        {
            var busy = new OperationState(freshState.SpawnSlots);
            busy.Units.Add(new Unit("t", UnitType.Transporter, Coordinate.Parse("A6"), 2));
            var best = SearchPlanner.Plan(map, clusters, costs, busy);
            Expect(best.Legs[0].Spawn == Coordinate.Parse("B6"), $"spawn {best.Legs[0].Spawn}");
        });

        // -- the state ------------------------------------------------------------------------

        Check("the free state read fills points, flags and units", () =>
        {
            var state = new OperationState(freshState.SpawnSlots);
            state.ApplyPull(JsonNode.Parse(PullReply)!);
            Expect(state.PointsUsed == 28 && state.PointsLeft == 272, $"points {state.PointsUsed}");
            Expect(state.TransportersUsed == 1 && state.ScoutsUsed == 3, "unit counters");
            Expect(state.HumanFound && state.HumanFoundAt == Coordinate.Parse("F2"), $"human {state.HumanFoundAt}");
            Expect(state.Units.Count == 2, $"{state.Units.Count} units");
            Expect(state.FindUnit("FEDCBA9876543210fedcba9876543210")?.Type == UnitType.Scout, "scout found by id, any case");
            Expect(state.NextFreeSpawnSlot() == Coordinate.Parse("A6"), "A6 free");
        });

        Check("getObjects (with its 'typ' spelling) replaces positions and keeps known loads; getLogs marks inspected fields", () =>
        {
            var state = new OperationState(freshState.SpawnSlots);
            state.Units.Add(new Unit("9d4c0ed78c955907b9a2b94a5f02007c", UnitType.Transporter, Coordinate.Parse("A6"), 1));
            state.ApplyObjects(JsonNode.Parse(ObjectsReply)!);
            Expect(state.Units.Count == 2, $"{state.Units.Count} units");
            Expect(state.FindUnit("9d4c0ed78c955907b9a2b94a5f02007c") is { Position: var at, PassengersAboard: 1 } && at == Coordinate.Parse("C9"), "transporter moved to C9, load kept");
            Expect(state.FindUnit("40cab23ecd9a5cfc5d1ffc840ebd1244")?.Type == UnitType.Scout, "scout parsed from 'typ'");

            state.ApplyLogs(JsonNode.Parse(LogsReply)!);
            Expect(state.Inspected.SetEquals([Coordinate.Parse("C8")]), $"inspected {string.Join(",", state.Inspected)}");
            Expect(state.Logs.Count == 1 && state.Logs[0].Message.StartsWith("Nie ma tu"), "log entry");
        });

        // -- the ledger -----------------------------------------------------------------------

        var ledgerPath = Path.Combine(Path.GetTempPath(), $"domatowo-ledger-{Guid.NewGuid():N}.json");
        try
        {
            Check("the ledger follows create and dismount replies and survives a restart", () =>
            {
                var ledger = new OperationLedger(ledgerPath);
                var state = MidOperation();
                ledger.Record(Answer("create", ("type", "transporter"), ("passengers", 2)), JsonNode.Parse("""{"object":"t1","crew":[{"id":"a"},{"id":"b"}]}""")!, state);
                Expect(ledger.AboardOf("t1") == 2, "after create");
                ledger.Record(Answer("dismount", ("object", "t1"), ("passengers", 1)), JsonNode.Parse("""{"object":"t1","dismounted":["a"],"spawned":[{"scout":"a","where":"C8"}]}""")!, state);
                Expect(ledger.AboardOf("t1") == 1, "after dismount");
                Expect(new OperationLedger(ledgerPath).AboardOf("t1") == 1, "reloaded");
                Expect(ledger.AboardOf("unknown") is null, "unknown transporter");
            });

            Check("a single transporter of unknown load is inferred from the counters", () =>
            {
                var ledger = new OperationLedger(ledgerPath + ".fresh");
                var state = MidOperation();
                state.Units[0] = state.Units[0] with { PassengersAboard = null };
                ledger.Resolve(state);
                Expect(state.Transporters.Single().PassengersAboard == 1, $"aboard {state.Transporters.Single().PassengersAboard}");
                Expect(ledger.AboardOf(state.Transporters.Single().Id) == 1, "remembered");
            });

            Check("inspections outlive the drained getLogs queue: an inspect marks the field, the log entry fills in the wording later", () =>
            {
                var ledger = new OperationLedger(ledgerPath + ".logs");
                var state = MidOperation(scoutAt: "C10", inspected: []);
                ledger.Record(Answer("inspect", ("object", "40cab23ecd9a5cfc5d1ffc840ebd1244")), JsonNode.Parse("""{"code":30,"object":"40cab23ecd9a5cfc5d1ffc840ebd1244","entries":1}""")!, state);
                Expect(ledger.InspectedFields.SequenceEqual([Coordinate.Parse("C10")]), "C10 marked right after inspect");

                ledger.Record(Answer("getLogs"), JsonNode.Parse("""{"code":60,"logs":[{"scout":"40cab23ecd9a5cfc5d1ffc840ebd1244","msg":"Pusto.","field":"C10"}]}""")!, state);
                Expect(ledger.Inspections.Count == 1 && ledger.Inspections[0].Message == "Pusto.", "placeholder replaced by the real entry");

                var later = new OperationLedger(ledgerPath + ".logs");
                var fresh = MidOperation(scoutAt: "C10", inspected: []);
                fresh.ApplyLogs(JsonNode.Parse("""{"logs":[{"scout":"40cab23ecd9a5cfc5d1ffc840ebd1244","msg":"Pusto.","field":"C10"},{"scout":"x","msg":"Nowy.","field":"B10"}]}""")!);
                later.Resolve(fresh);
                Expect(fresh.Inspected.SetEquals([Coordinate.Parse("C10"), Coordinate.Parse("B10")]), $"inspected after restart: {string.Join(",", fresh.Inspected)}");
                Expect(fresh.FreshLogs.Count == 1 && fresh.FreshLogs[0].Field == Coordinate.Parse("B10"), "only the unseen entry is fresh");
            });
        }
        finally
        {
            File.Delete(ledgerPath);
            File.Delete(ledgerPath + ".fresh");
            File.Delete(ledgerPath + ".logs");
        }

        // -- the tactician --------------------------------------------------------------------

        Check("the human confirmed ends the operation with the helicopter field", () =>
        {
            var state = MidOperation();
            state.HumanFound = true;
            state.HumanFoundAt = Coordinate.Parse("B11");
            Expect(Tactician.Decide(state, map, clusters, costs) is Decision.Finished { HumanAt: var at } && at == Coordinate.Parse("B11"), "finished at B11");
        });

        Check("the real mid-operation board: the scout dropped on C8 walks to C10 before anything else happens", () =>
        {
            var decision = Tactician.Decide(MidOperation(), map, clusters, costs);
            var move = ExpectAct(decision, "move");
            Expect(move["object"]?.GetValue<string>() == "40cab23ecd9a5cfc5d1ffc840ebd1244", "the scout, not the transporter");
            Expect(move["where"]?.GetValue<string>() == "C10", $"to {move["where"]}");
        });

        Check("a scout standing on an uninspected block inspects it", () =>
        {
            var state = MidOperation(scoutAt: "C10");
            var inspect = ExpectAct(Tactician.Decide(state, map, clusters, costs), "inspect");
            Expect(inspect["object"]?.GetValue<string>() == "40cab23ecd9a5cfc5d1ffc840ebd1244", "the scout");
        });

        Check("after inspecting, the scout steps to an adjacent uninspected block of the same cluster", () =>
        {
            var state = MidOperation(scoutAt: "C10", inspected: ["C8", "C10"]);
            var move = ExpectAct(Tactician.Decide(state, map, clusters, costs), "move");
            var target = Coordinate.Parse(move["where"]!.GetValue<string>());
            Expect(clusters[1].Contains(target) && target.DistanceTo(Coordinate.Parse("C10")) == 1, $"to {target}");
        });

        Check("with the first cluster done, the loaded transporter drives to the next cluster instead of the scout walking across town", () =>
        {
            var state = MidOperation(scoutAt: "C11", inspected: ["C8", "A10", "B10", "C10", "A11", "B11", "C11"]);
            var move = ExpectAct(Tactician.Decide(state, map, clusters, costs), "move");
            Expect(move["object"]?.GetValue<string>() == "9d4c0ed78c955907b9a2b94a5f02007c", "the transporter");
            Expect(move["where"]?.GetValue<string>() == "H9", $"to {move["where"]}");
        });

        Check("a loaded transporter parked at a stop of an unworked cluster lets one scout off", () =>
        {
            var state = MidOperation(transporterAt: "H9", scoutAt: "C11", inspected: ["C8", "A10", "B10", "C10", "A11", "B11", "C11"]);
            var dismount = ExpectAct(Tactician.Decide(state, map, clusters, costs), "dismount");
            Expect(dismount["passengers"]?.GetValue<int>() == 1, "one scout");
        });

        Check("a scout that landed north of the block walks in while the empty transporter waits", () =>
        {
            var state = MidOperation(transporterAt: "H9", aboard: 0, scoutAt: "C11", inspected: ["C8", "A10", "B10", "C10", "A11", "B11", "C11"]);
            state.Units.Add(new Unit("scout2", UnitType.Scout, Coordinate.Parse("H8")));
            var move = ExpectAct(Tactician.Decide(state, map, clusters, costs), "move");
            Expect(move["object"]?.GetValue<string>() == "scout2" && move["where"]?.GetValue<string>() == "H10", $"{move["object"]} to {move["where"]}");
        });

        Check("with no loaded transporter and the last cluster far from every scout, a new transporter is bought with one scout", () =>
        {
            var state = MidOperation(transporterAt: "H9", aboard: 0, scoutAt: "C11", inspected: ["C8", "A10", "B10", "C10", "A11", "B11", "C11", "H10", "I10", "H11", "I11"]);
            state.Units.Add(new Unit("scout2", UnitType.Scout, Coordinate.Parse("H11")));
            var create = ExpectAct(Tactician.Decide(state, map, clusters, costs), "create");
            Expect(create["type"]?.GetValue<string>() == "transporter" && create["passengers"]?.GetValue<int>() == 1, $"{create.ToJsonString()}");
        });

        Check("a fresh board starts with the plan's first leg: a transporter with two scouts", () =>
        {
            var create = ExpectAct(Tactician.Decide(new OperationState(freshState.SpawnSlots), map, clusters, costs), "create");
            Expect(create["passengers"]?.GetValue<int>() == 2, $"{create.ToJsonString()}");
        });

        Check("every block inspected and nobody found is reported as stuck, not as an action", () =>
        {
            var state = MidOperation(inspected: [.. clusters.SelectMany(cluster => cluster.Fields).Select(field => field.ToString()), "C8"]);
            Expect(Tactician.Decide(state, map, clusters, costs) is Decision.Stuck stuck && stuck.Reason.Contains("target symbol"), "stuck");
        });

        Check("with the transporter limit exhausted a scout walks to the last cluster; with no scout left the run is stuck", () =>
        {
            var state = MidOperation(transporterAt: "H9", aboard: 0, scoutAt: "C11", inspected: ["C8", "A10", "B10", "C10", "A11", "B11", "C11", "H10", "I10", "H11", "I11"]);
            state.TransportersUsed = 4;
            var move = ExpectAct(Tactician.Decide(state, map, clusters, costs), "move");
            Expect(move["object"]?.GetValue<string>() == "40cab23ecd9a5cfc5d1ffc840ebd1244" && clusters[0].Contains(Coordinate.Parse(move["where"]!.GetValue<string>())), $"scout walks to the top cluster: {move.ToJsonString()}");

            state.Units.RemoveAll(unit => unit.Type == UnitType.Scout);
            Expect(Tactician.Decide(state, map, clusters, costs) is Decision.Stuck, "stuck at the limit with nobody on foot");
        });

        OperationState MidOperation(string transporterAt = "C9", int? aboard = 1, string scoutAt = "C8", IEnumerable<string>? inspected = null)
        {
            var state = new OperationState(freshState.SpawnSlots) { Budget = 300 };
            state.Units.Add(new Unit("9d4c0ed78c955907b9a2b94a5f02007c", UnitType.Transporter, Coordinate.Parse(transporterAt), aboard));
            state.Units.Add(new Unit("40cab23ecd9a5cfc5d1ffc840ebd1244", UnitType.Scout, Coordinate.Parse(scoutAt)));
            state.TransportersUsed = 1;
            state.ScoutsUsed = 2;
            state.PointsUsed = 23;
            foreach (var field in inspected ?? ["C8"])
                state.Inspected.Add(Coordinate.Parse(field));
            return state;
        }

        JsonObject ExpectAct(Decision decision, string action)
        {
            if (decision is not Decision.Act act)
                throw new InvalidOperationException($"expected an action, got {decision}");
            if (act.Answer["action"]?.GetValue<string>() != action)
                throw new InvalidOperationException($"expected {action}, got {act.Summary}");
            return act.Answer;
        }

        // -- the guard ------------------------------------------------------------------------

        var guard = new ActionGuard(map, costs);

        OperationState Board()
        {
            var state = new OperationState(freshState.SpawnSlots);
            state.Units.Add(new Unit("t1", UnitType.Transporter, Coordinate.Parse("A6"), 3));
            state.Units.Add(new Unit("s1", UnitType.Scout, Coordinate.Parse("B10")));
            state.TransportersUsed = 1;
            state.ScoutsUsed = 3;
            state.PointsUsed = 20;
            return state;
        }

        Check("read-only actions pass for free; an unknown action does not", () =>
        {
            foreach (var action in new[] { "help", "getMap", "searchSymbol", "getLogs", "getObjects", "expenses", "actionCost" })
            {
                var verdict = guard.Check(Answer(action), Board());
                Expect(verdict.Accepted && verdict.Cost == 0, $"{action}: {verdict}");
            }
            Expect(!guard.Check(Answer("teleport"), Board()).Accepted, "teleport");
            Expect(!guard.Check(new JsonObject(), Board()).Accepted, "no action");
        });

        Check("reset is refused unless allowed by hand", () =>
        {
            Expect(!guard.Check(Answer("reset"), Board()).Accepted, "default");
            Expect(new ActionGuard(map, costs) { AllowReset = true }.Check(Answer("reset"), Board()).Accepted, "allowed");
        });

        Check("creating a transporter is priced per passenger and bounded by the limits", () =>
        {
            var ok = guard.Check(Answer("create", ("type", "transporter"), ("passengers", 3)), new OperationState(freshState.SpawnSlots));
            Expect(ok.Accepted && ok.Cost == 20, ok.ToString());
            Expect(!guard.Check(Answer("create", ("type", "transporter"), ("passengers", 5)), Board()).Accepted, "5 passengers");
            Expect(!guard.Check(Answer("create", ("type", "transporter")), Board()).Accepted, "no passengers");
            Expect(!guard.Check(Answer("create", ("type", "tank"), ("passengers", 1)), Board()).Accepted, "tank");

            var full = Board();
            full.ScoutsUsed = 6;
            Expect(!guard.Check(Answer("create", ("type", "transporter"), ("passengers", 3)), full).Accepted, "scout limit through passengers");
            full.TransportersUsed = 4;
            Expect(!guard.Check(Answer("create", ("type", "transporter"), ("passengers", 1)), full).Accepted, "transporter limit");
        });

        Check("creating a scout costs 5 and stops at 8 scouts or when every spawn slot is taken", () =>
        {
            var ok = guard.Check(Answer("create", ("type", "scout")), Board());
            Expect(ok.Accepted && ok.Cost == 5, ok.ToString());

            var full = Board();
            full.ScoutsUsed = 8;
            Expect(!guard.Check(Answer("create", ("type", "scout")), full).Accepted, "8 scouts");

            var crowded = Board();
            foreach (var slot in crowded.SpawnSlots.Skip(1))
                crowded.Units.Add(new Unit($"x{slot}", UnitType.Scout, slot));
            Expect(!guard.Check(Answer("create", ("type", "scout")), crowded).Accepted, "slots taken");
        });

        Check("a transporter may only drive to a street it can reach, priced per field", () =>
        {
            var ok = guard.Check(Answer("move", ("object", "t1"), ("where", "E2")), Board());
            Expect(ok.Accepted && ok.Cost == 8 && ok.Path!.Count == 8, ok.ToString());
            Expect(!guard.Check(Answer("move", ("object", "t1"), ("where", "F1")), Board()).Accepted, "to a block");
            Expect(!guard.Check(Answer("move", ("object", "t1"), ("where", "K6")), Board()).Accepted, "to empty ground");
            Expect(!guard.Check(Answer("move", ("object", "t1"), ("where", "A6")), Board()).Accepted, "staying put");
            Expect(!guard.Check(Answer("move", ("object", "t1"), ("where", "Z9")), Board()).Accepted, "off board");
            Expect(!guard.Check(Answer("move", ("object", "ghost"), ("where", "E2")), Board()).Accepted, "unknown unit");
        });

        Check("a scout walks anywhere at 7 per field", () =>
        {
            var ok = guard.Check(Answer("move", ("object", "s1"), ("where", "C11")), Board());
            Expect(ok.Accepted && ok.Cost == 14, ok.ToString());
        });

        Check("a move the budget cannot cover is refused before it is sent", () =>
        {
            var poor = Board();
            poor.PointsUsed = 295;
            Expect(!guard.Check(Answer("move", ("object", "t1"), ("where", "E2")), poor).Accepted, "8 pts with 5 left");
            Expect(guard.Check(Answer("move", ("object", "t1"), ("where", "B6")), poor).Accepted, "1 pt with 5 left");
        });

        Check("only a scout inspects, and it inspects where it stands", () =>
        {
            var ok = guard.Check(Answer("inspect", ("object", "s1")), Board());
            Expect(ok.Accepted && ok.Cost == 1 && ok.Reason.Contains("B10"), ok.ToString());
            Expect(!guard.Check(Answer("inspect", ("object", "t1")), Board()).Accepted, "transporter");
        });

        Check("dismount needs a transporter with enough scouts aboard and room around it", () =>
        {
            var ok = guard.Check(Answer("dismount", ("object", "t1"), ("passengers", 2)), Board());
            Expect(ok.Accepted && ok.Cost == 0, ok.ToString());
            Expect(!guard.Check(Answer("dismount", ("object", "t1"), ("passengers", 4)), Board()).Accepted, "4 of 3 aboard");
            Expect(!guard.Check(Answer("dismount", ("object", "s1"), ("passengers", 1)), Board()).Accepted, "scout dismounting");

            // A6 sits on the board's edge with three neighbours, so four scouts could never step off there.
            Expect(Coordinate.Parse("A6").Neighbours().Count() == 3, "edge geometry");
            Expect(guard.Check(Answer("dismount", ("object", "t1"), ("passengers", 3)), Board()).Accepted, "3 of 3 aboard, 3 fields around");

            var unknownLoad = Board();
            unknownLoad.Units[0] = unknownLoad.Units[0] with { PassengersAboard = null, Position = Coordinate.Parse("D6") };
            Expect(guard.Check(Answer("dismount", ("object", "t1"), ("passengers", 4)), unknownLoad).Accepted, "unknown load passes the count check");

            var boxedIn = Board();
            foreach (var field in Coordinate.Parse("A6").Neighbours())
                boxedIn.Units.Add(new Unit($"x{field}", UnitType.Scout, field));
            Expect(!guard.Check(Answer("dismount", ("object", "t1"), ("passengers", 1)), boxedIn).Accepted, "no free field around");
        });

        Check("the helicopter flies only to the field where a scout confirmed the human", () =>
        {
            Expect(!guard.Check(Answer("callHelicopter", ("destination", "F2")), Board()).Accepted, "nobody found yet");

            var found = Board();
            found.HumanFound = true;
            found.HumanFoundAt = Coordinate.Parse("F2");
            Expect(guard.Check(Answer("callHelicopter", ("destination", "F2")), found).Accepted, "right field");
            Expect(!guard.Check(Answer("callHelicopter", ("destination", "G2")), found).Accepted, "wrong field");
            Expect(!guard.Check(Answer("callHelicopter"), found).Accepted, "no destination");

            var vague = Board();
            vague.HumanFound = true;
            Expect(!guard.Check(Answer("callHelicopter", ("destination", "F2")), vague).Accepted, "found but where unknown");
        });

        Console.WriteLine();
        Console.WriteLine($"{total - failures}/{total} checks passed.");
        return failures == 0;
    }

    private static JsonObject Answer(string action, params (string Key, object Value)[] fields)
    {
        var answer = new JsonObject { ["action"] = action };
        foreach (var (key, value) in fields)
            answer[key] = value is int number ? JsonValue.Create(number) : JsonValue.Create(value.ToString());
        return answer;
    }

    private static void Expect(bool condition, string detail)
    {
        if (!condition)
            throw new InvalidOperationException(detail);
    }

    private static void ExpectThrows<TException>(Action action, string detail) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{detail}: expected {typeof(TException).Name}");
    }
}
