using _04_05_zadanie.Agents;
using _04_05_zadanie.Hub;
using _04_05_zadanie.Llm;
using _04_05_zadanie.Mission;
using _04_05_zadanie.Tools;
using _04_05_zadanie.Warehouse;

namespace _04_05_zadanie.Tests;

/// <summary>
/// The rules that stand between the agent and the hub, checked offline on made-up data so that
/// nothing about the real answer is written into code. Every case is a way a run could have been
/// wasted: a query the hub refuses, a page mistaken for a whole table, an order that does not match.
/// </summary>
public static class OfflineTests
{
    private const string SampleDemand = """
        {
          "komarowo": { "cegla": 40, "woda": 12 },
          "Zarnowiec": { "deska": 7 }
        }
        """;

    private const string SampleRowsReply = """
        {
          "code": 170, "message": "Database query executed.", "tool": "database", "mode": "read-only",
          "query": "select * from people", "table": "people", "totalTableRows": 45,
          "columns": ["person_id", "login", "birthday", "is_active"],
          "rows": [
            { "person_id": 1, "login": "jkowalski", "birthday": "1971-01-01", "is_active": 1 },
            { "person_id": null, "login": "ghost", "birthday": "1990-05-05", "is_active": 1 }
          ],
          "count": 2, "limit": 30
        }
        """;

    private const string SampleSchemaReply = """
        {
          "code": 165, "message": "Full database schema loaded.", "tool": "database", "mode": "read-only", "query": ".schema",
          "schemas": [
            { "table": "places", "schema": "CREATE TABLE places(place_id int, name varchar(80))" },
            { "table": "people", "schema": "CREATE TABLE people(person_id int, login varchar(20))" }
          ],
          "count": 2
        }
        """;

    private const string SampleOrdersReply = """
        {
          "code": 101, "message": "Orders loaded.",
          "orders": [
            { "id": "aaa111", "title": "Do kuchni", "creatorID": 2, "destination": 106710, "signature": "9fde",
              "items": [ { "name": "cegla", "items": 40 }, { "name": "woda", "items": 12 } ] },
            { "id": "bbb222", "title": "Warsztat", "creatorID": 5, "destination": 181279, "signature": "77ac",
              "items": [] }
          ],
          "count": 2
        }
        """;

    public static bool Run()
    {
        var results = new List<(string Case, bool Passed, string Detail)>();

        void Expect(string name, bool passed, string detail = "") => results.Add((name, passed, detail));

        void ExpectAllowed(string name, string query)
        {
            var verdict = QueryGuard.Evaluate(query);
            Expect(name, verdict.Allowed, verdict.Reason);
        }

        void ExpectRefused(string name, string query, string fragment)
        {
            var verdict = QueryGuard.Evaluate(query);
            Expect(name, !verdict.Allowed && verdict.Reason.Contains(fragment, StringComparison.OrdinalIgnoreCase), verdict.Allowed ? "allowed" : verdict.Reason);
        }

        void ExpectThrows(string name, Action action, string fragment)
        {
            try
            {
                action();
                Expect(name, false, "no exception");
            }
            catch (Exception ex)
            {
                Expect(name, ex.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase), ex.Message);
            }
        }

        // --- query guard: what the help allows --------------------------------------------------------
        ExpectAllowed("guard: plain select", "select * from users");
        ExpectAllowed("guard: select with != filter", "SELECT user_id FROM users WHERE is_active != 1");
        ExpectAllowed("guard: select with paging", "select * from destinations limit 30 offset 30");
        ExpectAllowed("guard: select with leading whitespace", "   select count(*) from users");
        ExpectAllowed("guard: select with is null", "select login from users where user_id is null");
        ExpectAllowed("guard: show tables", "show tables");
        ExpectAllowed("guard: show create table", "SHOW CREATE TABLE users");
        ExpectAllowed("guard: .tables", ".tables");
        ExpectAllowed("guard: .schema", ".schema");
        ExpectAllowed("guard: .schema table", ".schema users");

        // --- query guard: what would waste a request ---------------------------------------------------
        ExpectRefused("guard: empty", "   ", "empty");
        ExpectRefused("guard: delete", "delete from users", "read-only");
        ExpectRefused("guard: insert", "insert into users values (1)", "read-only");
        ExpectRefused("guard: pragma", "pragma table_info(users)", "read-only");
        ExpectRefused("guard: cte is not a plain select", "with x as (select 1) select * from x", "read-only");
        ExpectRefused("guard: second statement", "select 1; drop table users", "';'");
        ExpectRefused("guard: select into", "select * into backup from users", "INTO");
        ExpectRefused("guard: select with subquery keyword", "select * from users where 1 = (select 1 union select 2 from sqlite_master where 0 or (1 = 1 and 'x' = 'x') ) and role in (select role_id from roles) or 0 = 0 and 1 = (delete)", "DELETE");
        ExpectRefused("guard: <> is an html tag to the hub", "select * from users where role <> 2", "!=");
        ExpectRefused("guard: > is an html tag to the hub", "select * from users where user_id > 5", "'>'");
        ExpectRefused("guard: comment", "select * from users -- all", "comment");
        ExpectRefused("guard: .schema with two names", ".schema users roles", "read-only");
        Expect("guard: keeps the query text", QueryGuard.Evaluate("  select 1 ").Query == "select 1", QueryGuard.Evaluate("  select 1 ").Query);

        // --- query result --------------------------------------------------------------------------------
        var rows = QueryResult.Parse(SampleRowsReply);
        Expect("result: table name", rows.Table == "people", rows.Table ?? "null");
        Expect("result: columns", rows.Columns.SequenceEqual(["person_id", "login", "birthday", "is_active"]), string.Join(",", rows.Columns));
        Expect("result: count and total", rows.Count == 2 && rows.TotalTableRows == 45 && rows.Limit == 30, $"{rows.Count}/{rows.TotalTableRows}/{rows.Limit}");
        Expect("result: short page is not truncated", !rows.MayBeTruncated);
        Expect("result: int cell", QueryResult.ReadInt(rows.Rows[0], "person_id") == 1);
        Expect("result: null cell reads as null", QueryResult.ReadInt(rows.Rows[1], "person_id") is null);
        Expect("result: missing column reads as null", QueryResult.ReadInt(rows.Rows[0], "nope") is null);
        Expect("result: string cell", QueryResult.ReadString(rows.Rows[0], "birthday") == "1971-01-01");
        Expect("result: render shows NULL", rows.Render().Contains("NULL | ghost"), rows.Render());
        Expect("result: render shows whole-table size", rows.Render().Contains("whole table: 45"), rows.Render());

        var fullPage = QueryResult.Parse(SampleRowsReply.Replace("\"count\": 2, \"limit\": 30", "\"count\": 30, \"limit\": 30"));
        Expect("result: full page may be truncated", fullPage.MayBeTruncated);
        Expect("result: full page render tells how to continue", fullPage.Render().Contains("OFFSET 30"), fullPage.Render());

        var schema = QueryResult.Parse(SampleSchemaReply);
        Expect("result: schema entries", schema.Schemas.Count == 2 && schema.Schemas[1].Table == "people", string.Join(",", schema.Schemas.Select(s => s.Table)));
        Expect("result: schema render", schema.Render().Contains("CREATE TABLE places"), schema.Render());
        Expect("result: schema is never truncated", !schema.MayBeTruncated);

        // --- demand ----------------------------------------------------------------------------------------
        var demand = Demand.Parse(SampleDemand);
        Expect("demand: cities", demand.Cities.Count == 2, demand.Cities.Count.ToString());
        Expect("demand: city names folded", demand.Cities[1].City == "zarnowiec", demand.Cities[1].City);
        Expect("demand: items", demand.Cities[0].Items["cegla"] == 40 && demand.Cities[0].Items["woda"] == 12);
        Expect("demand: find is case-insensitive", demand.Find("Komarowo")?.City == "komarowo");
        Expect("demand: find folds diacritics", demand.Find("Żarnowiec")?.City == "zarnowiec");
        Expect("demand: find unknown", demand.Find("Puck") is null);
        ExpectThrows("demand: zero quantity", () => Demand.Parse("""{ "a": { "x": 0 } }"""), "positive integer");
        ExpectThrows("demand: fractional quantity", () => Demand.Parse("""{ "a": { "x": 1.5 } }"""), "positive integer");
        ExpectThrows("demand: text quantity", () => Demand.Parse("""{ "a": { "x": "5" } }"""), "positive integer");
        ExpectThrows("demand: empty city", () => Demand.Parse("""{ "a": { } }"""), "no items");
        ExpectThrows("demand: duplicate city", () => Demand.Parse("""{ "a": { "x": 1 }, "A": { "y": 2 } }"""), "twice");
        ExpectThrows("demand: no cities", () => Demand.Parse("{}"), "no cities");
        ExpectThrows("demand: not an object", () => Demand.Parse("[]"), "object");

        // --- text normalisation ----------------------------------------------------------------------------
        Expect("fold: diacritics", TextNormalizer.Fold("Żarnowiec") == "zarnowiec", TextNormalizer.Fold("Żarnowiec"));
        Expect("fold: l with stroke", TextNormalizer.Fold("Łeba") == "leba", TextNormalizer.Fold("Łeba"));
        Expect("fold: whitespace and case", TextNormalizer.Fold("  PUCK ") == "puck");
        Expect("fold: empty never matches", !TextNormalizer.SameName("", ""));
        Expect("fold: same name", TextNormalizer.SameName("Domatowo", "domatowo"));

        // --- orders --------------------------------------------------------------------------------------
        var orders = OrderBook.Parse(SampleOrdersReply);
        Expect("orders: count", orders.Count == 2, orders.Count.ToString());
        Expect("orders: fields", orders[0].Id == "aaa111" && orders[0].CreatorId == 2 && orders[0].Destination == 106710 && orders[0].Signature == "9fde");
        Expect("orders: items", orders[0].Items["cegla"] == 40 && orders[0].Items["woda"] == 12);
        Expect("orders: empty items", orders[1].Items.Count == 0);
        Expect("orders: single order reply", OrderBook.Parse("""{ "order": { "id": "x", "title": "t", "items": { "woda": 3 } } }""") is [{ Id: "x" } single] && single.Items["woda"] == 3);
        Expect("orders: bare order reply", OrderBook.Parse("""{ "id": "y", "title": "t", "items": [ { "name": "woda", "items": 2 }, { "name": "woda", "items": 3 } ] }""") is [{ Id: "y" } bare] && bare.Items["woda"] == 5);
        Expect("orders: unknown shape is empty", OrderBook.Parse("""{ "code": 1 }""").Count == 0);
        Expect("orders: render", OrderBook.Render(orders).Contains("cegla 40") && OrderBook.Render(orders).Contains("(empty)"), OrderBook.Render(orders));

        var komarowo = demand.Cities[0];
        Expect("compare: exact match", OrderBook.Differences(orders[0], komarowo).Count == 0, string.Join("; ", OrderBook.Differences(orders[0], komarowo)));
        Expect("compare: empty order lists every line", OrderBook.Differences(orders[1], komarowo).Count == 2, string.Join("; ", OrderBook.Differences(orders[1], komarowo)));

        var doubled = orders[0] with { Items = new Dictionary<string, int> { ["cegla"] = 80, ["woda"] = 12 } };
        Expect("compare: doubled quantity", OrderBook.Differences(doubled, komarowo) is [var d] && d.Contains("cegla is 80"), string.Join("; ", OrderBook.Differences(doubled, komarowo)));

        var extra = orders[0] with { Items = new Dictionary<string, int> { ["cegla"] = 40, ["woda"] = 12, ["kilof"] = 1 } };
        Expect("compare: extra item", OrderBook.Differences(extra, komarowo) is [var e] && e.Contains("extra kilof"), string.Join("; ", OrderBook.Differences(extra, komarowo)));

        var missing = orders[0] with { Items = new Dictionary<string, int> { ["cegla"] = 40 } };
        Expect("compare: missing item", OrderBook.Differences(missing, komarowo) is [var m] && m.Contains("missing woda 12"), string.Join("; ", OrderBook.Differences(missing, komarowo)));

        // --- observations: rows recognised by column name --------------------------------------------------
        var observations = new Observations();
        var destinationsSummary = observations.Absorb(QueryResult.Parse("""
            { "columns": ["destination_id", "name"], "rows": [
                { "destination_id": 1001, "name": "Komarowo" }, { "destination_id": 1002, "name": "Żarnowiec" }, { "destination_id": 1003, "name": "Puck" },
                { "destination_id": null, "name": "Nowhere" } ], "count": 4, "limit": 30 }
            """));
        Expect("observe: destinations recorded", destinationsSummary.Destinations == 3 && destinationsSummary.Unrecognised == 1, destinationsSummary.Render());
        Expect("observe: destination lookup", observations.FindDestination(1002)?.Name == "Żarnowiec");

        var usersSummary = observations.Absorb(QueryResult.Parse("""
            { "columns": ["user_id", "login", "role"], "rows": [
                { "user_id": 7, "login": "jkowalski", "role": 2 }, { "user_id": null, "login": "ghost", "role": 6 },
                { "user_id": 9, "login": "asleep", "role": 2 }, { "user_id": 11, "login": "intern", "role": 3 }, { "user_id": 12, "login": "noborn", "role": 2 } ], "count": 5, "limit": 30 }
            """));
        Expect("observe: users recorded", usersSummary.Users == 5, usersSummary.Render());
        Expect("observe: birthday not selected yet", observations.FindUserById(7)?.Birthday is null);

        observations.Absorb(QueryResult.Parse("""
            { "columns": ["login", "birthday", "is_active"], "rows": [
                { "login": "jkowalski", "birthday": "1971-01-01", "is_active": 1 }, { "login": "asleep", "birthday": "1980-02-02", "is_active": 0 },
                { "login": "intern", "birthday": "2001-03-03", "is_active": 1 }, { "login": "ghost", "birthday": "1990-05-05", "is_active": 1 } ], "count": 4, "limit": 30 }
            """));
        Expect("observe: partial rows merged by login", observations.FindUserById(7) is { Birthday: "1971-01-01", IsActive: 1, Role: 2 }, observations.FindUserById(7)?.Describe() ?? "null");
        Expect("observe: merge does not duplicate users", observations.Users.Count == 5, observations.Users.Count.ToString());
        Expect("observe: null-id user is findable by login only", observations.FindUserByLogin("ghost") is { UserId: null, Birthday: "1990-05-05" });

        var rolesSummary = observations.Absorb(QueryResult.Parse("""
            { "columns": ["role_id", "name"], "rows": [ { "role_id": 2, "name": "Transport" }, { "role_id": 3, "name": "Praktykant" } ], "count": 2, "limit": 30 }
            """));
        Expect("observe: roles recorded", rolesSummary.Roles == 2 && observations.DescribeRole(3) == "3 (Praktykant)", observations.DescribeRole(3));

        var aliased = observations.Absorb(QueryResult.Parse("""{ "columns": ["id", "who"], "rows": [ { "id": 7, "who": "jkowalski" } ], "count": 1, "limit": 30 }"""));
        Expect("observe: aliased columns are not recognised", aliased.Unrecognised == 1 && aliased.Users == 0 && aliased.Render().Contains("column names"), aliased.Render());
        Expect("observe: schema reply records nothing", observations.Absorb(schema).Render().Contains("Nothing"));

        Expect("observe: orders not listed yet", !observations.OrdersListed);
        observations.AbsorbOrders(orders);
        Expect("observe: orders listed", observations.OrdersListed && observations.Orders.Count == 2);

        var roundTrip = Observations.FromJson(observations.ToJson());
        Expect("observe: json round trip", roundTrip.FindDestination(1001)?.Name == "Komarowo" && roundTrip.FindUserById(7)?.Birthday == "1971-01-01"
            && roundTrip.Users.Count == 5 && roundTrip.Orders.Count == 2 && roundTrip.OrdersListed && roundTrip.DescribeRole(2) == "2 (Transport)");

        // --- plan ------------------------------------------------------------------------------------------
        var plan = new OrderPlan();
        plan.Upsert(new PlannedOrder("Komarowo", "", 1001, 7, "jkowalski", "1971-01-01"));
        plan.Upsert(new PlannedOrder("zarnowiec", "Deski", 1002, 7, "jkowalski", "1971-01-01"));
        Expect("plan: city folded and default title", plan.Find("komarowo") is { City: "komarowo", Title: "Dostawa zaopatrzenia: Komarowo" }, plan.Find("komarowo")?.Title ?? "null");
        plan.Upsert(new PlannedOrder("KOMAROWO", "Cegly", 1001, 7, "jkowalski", "1971-01-01"));
        Expect("plan: upsert replaces by city", plan.Orders.Count == 2 && plan.Find("komarowo")?.Title == "Cegly", plan.Render());
        Expect("plan: json round trip", OrderPlan.FromJson(plan.ToJson()).Find("zarnowiec")?.DestinationId == 1002);
        Expect("plan: remove", plan.Remove("Zarnowiec") && plan.Orders.Count == 1 && !plan.Remove("nope"));
        plan.Upsert(new PlannedOrder("zarnowiec", "Deski", 1002, 7, "jkowalski", "1971-01-01"));

        // --- plan validator: the seeded creator (order aaa111, creatorID 2) is not observed as a user here ---
        var seededUsers = new Observations();
        seededUsers.Absorb(QueryResult.Parse("""{ "columns": ["user_id", "login", "role"], "rows": [ { "user_id": 2, "login": "seed", "role": 2 }, { "user_id": 5, "login": "seed2", "role": 2 } ], "count": 2, "limit": 30 }"""));

        var report = PlanValidator.Validate(plan, demand, observations);
        Expect("validate: complete plan is valid", report.IsValid, report.Render());
        Expect("validate: seeded creators unseen gives a warning", report.Warnings.Count == 1 && report.Warnings[0].Contains("could not be compared"), report.Render());

        var withSeeds = Observations.FromJson(observations.ToJson());
        withSeeds.Absorb(QueryResult.Parse("""{ "columns": ["user_id", "login", "role"], "rows": [ { "user_id": 2, "login": "seed", "role": 2 }, { "user_id": 5, "login": "seed2", "role": 2 } ], "count": 2, "limit": 30 }"""));
        report = PlanValidator.Validate(plan, demand, withSeeds);
        Expect("validate: role matches seeded creators, no warning", report.IsValid && report.Warnings.Count == 0, report.Render());

        var internPlan = OrderPlan.FromJson(plan.ToJson());
        internPlan.Upsert(new PlannedOrder("zarnowiec", "Deski", 1002, 11, "intern", "2001-03-03"));
        report = PlanValidator.Validate(internPlan, demand, withSeeds);
        Expect("validate: different role is a warning, not an error", report.IsValid && report.Warnings is [var roleWarning] && roleWarning.Contains("3 (Praktykant)") && roleWarning.Contains("2 (Transport)"), report.Render());

        var unlisted = new Observations();
        unlisted.Absorb(QueryResult.Parse("""{ "columns": ["destination_id", "name"], "rows": [ { "destination_id": 1001, "name": "Komarowo" }, { "destination_id": 1002, "name": "Zarnowiec" } ], "count": 2, "limit": 30 }"""));
        unlisted.Absorb(QueryResult.Parse("""{ "columns": ["user_id", "login", "birthday", "is_active"], "rows": [ { "user_id": 7, "login": "jkowalski", "birthday": "1971-01-01", "is_active": 1 } ], "count": 1, "limit": 30 }"""));
        report = PlanValidator.Validate(plan, demand, unlisted);
        Expect("validate: orders never listed and role never selected are warnings", report.IsValid && report.Warnings.Count == 3 && report.Warnings[0].Contains("never listed") && report.Warnings[1].Contains("never selected"), report.Render());

        void ExpectError(string name, OrderPlan candidate, Observations facts, string fragment)
        {
            var verdict = PlanValidator.Validate(candidate, demand, facts);
            Expect(name, !verdict.IsValid && verdict.Errors.Any(e => e.Contains(fragment, StringComparison.OrdinalIgnoreCase)), verdict.Render());
        }

        OrderPlan Variant(PlannedOrder replacement)
        {
            var copy = OrderPlan.FromJson(plan.ToJson());
            copy.Upsert(replacement);
            return copy;
        }

        var missingCity = new OrderPlan();
        missingCity.Upsert(plan.Find("komarowo")!);
        ExpectError("validate: missing city", missingCity, withSeeds, "No order for zarnowiec");

        var extraCity = OrderPlan.FromJson(plan.ToJson());
        extraCity.Upsert(new PlannedOrder("puck", "Puck", 1003, 7, "jkowalski", "1971-01-01"));
        ExpectError("validate: city outside the demand", extraCity, withSeeds, "not a city of the demand file");

        ExpectError("validate: destination never observed", Variant(new PlannedOrder("zarnowiec", "t", 9999, 7, "jkowalski", "1971-01-01")), withSeeds, "not seen in any query result");
        ExpectError("validate: destination of another city", Variant(new PlannedOrder("zarnowiec", "t", 1003, 7, "jkowalski", "1971-01-01")), withSeeds, "is Puck, not zarnowiec");
        ExpectError("validate: creator never observed", Variant(new PlannedOrder("zarnowiec", "t", 1002, 42, "jkowalski", "1971-01-01")), withSeeds, "creatorID 42 was not seen");
        ExpectError("validate: login of another user", Variant(new PlannedOrder("zarnowiec", "t", 1002, 7, "ghost", "1971-01-01")), withSeeds, "does not belong to user_id 7");
        ExpectError("validate: wrong birthday", Variant(new PlannedOrder("zarnowiec", "t", 1002, 7, "jkowalski", "1971-01-02")), withSeeds, "differs from the observed 1971-01-01");
        ExpectError("validate: malformed birthday", Variant(new PlannedOrder("zarnowiec", "t", 1002, 7, "jkowalski", "1.1.1971")), withSeeds, "YYYY-MM-DD");
        ExpectError("validate: inactive creator", Variant(new PlannedOrder("zarnowiec", "t", 1002, 9, "asleep", "1980-02-02")), withSeeds, "not active");
        ExpectError("validate: birthday never selected", Variant(new PlannedOrder("zarnowiec", "t", 1002, 12, "noborn", "1990-01-01")), withSeeds, "birthday of user_id 12 was never selected");
        ExpectError("validate: is_active never selected", Variant(new PlannedOrder("zarnowiec", "t", 1002, 12, "noborn", "1990-01-01")), withSeeds, "is_active of user_id 12 was never selected");
        Expect("validate: one order is judged alone", PlanValidator.OrderErrors(new PlannedOrder("komarowo", "t", 1001, 7, "jkowalski", "1971-01-01"), withSeeds).Count == 0);

        // --- tools and hooks on the state (no network) ---------------------------------------------------
        var state = new MissionState(demand);
        state.Observations.Absorb(QueryResult.Parse("""{ "columns": ["destination_id", "name"], "rows": [ { "destination_id": 1001, "name": "Komarowo" }, { "destination_id": 1002, "name": "Zarnowiec" }, { "destination_id": 1003, "name": "Puck" } ], "count": 3, "limit": 30 }"""));
        state.Observations.Absorb(QueryResult.Parse("""{ "columns": ["user_id", "login", "birthday", "role", "is_active"], "rows": [ { "user_id": 7, "login": "jkowalski", "birthday": "1971-01-01", "role": 2, "is_active": 1 }, { "user_id": 11, "login": "intern", "birthday": "2001-03-03", "role": 3, "is_active": 1 }, { "user_id": 2, "login": "seed", "birthday": "1960-01-01", "role": 2, "is_active": 1 } ], "count": 3, "limit": 30 }"""));
        state.Observations.Absorb(QueryResult.Parse("""{ "columns": ["role_id", "name"], "rows": [ { "role_id": 2, "name": "Transport" }, { "role_id": 3, "name": "Praktykant" } ], "count": 2, "limit": 30 }"""));

        var register = new RegisterOrdersTool(state);
        var check = new CheckPlanTool(state);
        var hooks = new WarehouseHooks(state, new FoodwarehouseClient("http://localhost", "key", Path.Combine(Path.GetTempPath(), "foodwarehouse-tests.jsonl"), 1, 0));
        var registerCall = new ToolCall { Id = "call-1", FunctionName = RegisterOrdersTool.ToolName, ArgumentsJson = "{}" };

        Expect("hook: registration refused before orders are listed", hooks.BeforeToolCallAsync(registerCall).Result?.Contains("list_orders") == true);
        state.Observations.AbsorbOrders(orders);
        Expect("hook: registration allowed once orders are listed", hooks.BeforeToolCallAsync(registerCall).Result is null);
        Expect("hook: finish refused while the plan is empty", hooks.BeforeFinishAsync().Result?.Contains("No order for komarowo") == true);

        var refusedEmpty = register.ExecuteAsync("""{ "orders": [] }""").Result;
        Expect("register: empty array refused", refusedEmpty.Contains("non-empty"), refusedEmpty);

        var mixed = register.ExecuteAsync("""
            { "orders": [
                { "city": "Komarowo", "destination_id": 1001, "creator_id": 7, "login": "jkowalski", "birthday": "1971-01-01" },
                { "city": "zarnowiec", "destination_id": 9999, "creator_id": 7, "login": "jkowalski", "birthday": "1971-01-01" },
                { "city": "puck", "destination_id": 1003, "creator_id": 7, "login": "jkowalski", "birthday": "1971-01-01" },
                { "city": "zarnowiec", "destination_id": 1002, "creator_id": "7", "login": "jkowalski" } ] }
            """).Result;
        Expect("register: valid entry accepted, others refused with reasons", mixed.StartsWith("Accepted 1 entry, refused 3.") && mixed.Contains("9999 was not seen") && mixed.Contains("not a city of the demand list") && mixed.Contains("missing or mistyped birthday"), mixed);
        Expect("register: counters", state.Registrations == 1 && state.RegistrationRefusals == 3 && state.Plan.Orders.Count == 1);
        Expect("register: default title applied", state.Plan.Find("komarowo")?.Title == "Dostawa zaopatrzenia: Komarowo", state.Plan.Find("komarowo")?.Title ?? "null");

        var incomplete = check.ExecuteAsync("{}").Result;
        Expect("check: incomplete plan not accepted", !state.Accepted && incomplete.Contains("No order for zarnowiec") && incomplete.Contains("Fix the errors"), incomplete);

        register.ExecuteAsync("""{ "orders": [ { "city": "zarnowiec", "destination_id": 1002, "creator_id": 11, "login": "intern", "birthday": "2001-03-03", "title": "Deski" } ] }""").Wait();
        var withWarning = check.ExecuteAsync("{}").Result;
        Expect("check: valid with warnings is not accepted by default", !state.Accepted && withWarning.Contains("WARNING") && withWarning.Contains("accept_warnings=true"), withWarning);
        Expect("hook: finish allowed when the plan is valid", hooks.BeforeFinishAsync().Result is null);

        var acceptedWithWarning = check.ExecuteAsync("""{ "accept_warnings": true }""").Result;
        Expect("check: warnings accepted explicitly", state.Accepted && acceptedWithWarning.Contains("accepted"), acceptedWithWarning);

        register.ExecuteAsync("""{ "orders": [ { "city": "zarnowiec", "destination_id": 1002, "creator_id": 7, "login": "jkowalski", "birthday": "1971-01-01" } ] }""").Wait();
        Expect("register: a change withdraws the acceptance", !state.Accepted);
        var clean = check.ExecuteAsync("{}").Result;
        Expect("check: clean plan accepted without consent", state.Accepted && !clean.Contains("WARNING"), clean);
        Expect("hook: progress line", state.RenderProgress().Contains("2/2 cities registered"), state.RenderProgress());

        // --- execution checks: replies of every shape, and the final comparison ---------------------------
        Expect("exec: signature from hash", ExecutionChecks.ParseSignature("""{ "code": 130, "hash": "9FDE570FCA1CFEE02E8DEB807EC7FF6E9F2670DE" }""") == "9fde570fca1cfee02e8deb807ec7ff6e9f2670de");
        Expect("exec: signature from signature field", ExecutionChecks.ParseSignature("""{ "signature": "77acdf36ae70d9fe067a053360a72cd734407bbf" }""") == "77acdf36ae70d9fe067a053360a72cd734407bbf");
        Expect("exec: signature of wrong length rejected", ExecutionChecks.ParseSignature("""{ "hash": "abc" }""") is null);
        Expect("exec: signature from garbage", ExecutionChecks.ParseSignature("not json") is null);
        Expect("exec: created id at root", ExecutionChecks.ParseCreatedId("""{ "code": 102, "id": "ed2f3696d8bcafe2437cdc88f7515037" }""") == "ed2f3696d8bcafe2437cdc88f7515037");
        Expect("exec: created id under order", ExecutionChecks.ParseCreatedId("""{ "order": { "id": "abc" } }""") == "abc");
        Expect("exec: created orderID variant", ExecutionChecks.ParseCreatedId("""{ "orderID": 17 }""") == "17");
        Expect("exec: created id missing", ExecutionChecks.ParseCreatedId("""{ "message": "ok" }""") is null);

        var placedPlan = new OrderPlan();
        placedPlan.Upsert(new PlannedOrder("komarowo", "Cegly", 106710, 2, "seed", "1960-01-01"));
        placedPlan.Upsert(new PlannedOrder("zarnowiec", "Deski", 181279, 5, "seed2", "1961-01-01"));
        var signatures = new Dictionary<string, string> { ["komarowo"] = "9fde", ["zarnowiec"] = "77ac" };
        var placed = OrderBook.Parse("""
            { "orders": [
                { "id": "aaa111", "title": "Cegly", "creatorID": 2, "destination": 106710, "signature": "9FDE", "items": [ { "name": "cegla", "items": 40 }, { "name": "woda", "items": 12 } ] },
                { "id": "bbb222", "title": "Deski", "creatorID": 5, "destination": 181279, "signature": "77ac", "items": [ { "name": "deska", "items": 7 } ] } ] }
            """);
        Expect("exec: find order by destination, creator and signature (case-insensitive)", ExecutionChecks.FindOrder(placed, placedPlan.Find("komarowo")!, "9fde")?.Id == "aaa111");
        Expect("exec: find order rejects another signature", ExecutionChecks.FindOrder(placed, placedPlan.Find("komarowo")!, "0000") is null);
        Expect("exec: final state matches", ExecutionChecks.VerifyFinal(placed, placedPlan, demand, signatures).Count == 0, string.Join("; ", ExecutionChecks.VerifyFinal(placed, placedPlan, demand, signatures)));

        var doubledFinal = new List<WarehouseOrder> { placed[0] with { Items = new Dictionary<string, int> { ["cegla"] = 80, ["woda"] = 12 } }, placed[1] };
        Expect("exec: doubled quantity is a problem", ExecutionChecks.VerifyFinal(doubledFinal, placedPlan, demand, signatures) is [var doubledProblem] && doubledProblem.Contains("cegla is 80"), string.Join("; ", ExecutionChecks.VerifyFinal(doubledFinal, placedPlan, demand, signatures)));

        var seededFinal = new List<WarehouseOrder>(placed) { new("ccc333", "Kopalnia", 7, 369482, "b343", new Dictionary<string, int> { ["kilof"] = 1 }) };
        Expect("exec: a seeded order to another destination is not judged", ExecutionChecks.VerifyFinal(seededFinal, placedPlan, demand, signatures).Count == 0, string.Join("; ", ExecutionChecks.VerifyFinal(seededFinal, placedPlan, demand, signatures)));
        Expect("exec: only orders to planned destinations are cleared", ExecutionChecks.OrdersToPlannedDestinations(seededFinal, placedPlan) is [{ Id: "aaa111" }, { Id: "bbb222" }]);

        var duplicateFinal = new List<WarehouseOrder>(placed) { new("ddd444", "Cegly bis", 2, 106710, "0000", new Dictionary<string, int> { ["cegla"] = 40 }) };
        var duplicateProblems = ExecutionChecks.VerifyFinal(duplicateFinal, placedPlan, demand, signatures);
        Expect("exec: a second order to a planned destination is a problem", duplicateProblems.Count == 2 && duplicateProblems.Any(p => p.Contains("Unexpected order ddd444")) && duplicateProblems.Any(p => p.Contains("3 order(s) to the planned destinations in the warehouse, 2 planned")), string.Join("; ", duplicateProblems));

        var missingFinal = new List<WarehouseOrder> { placed[0] };
        Expect("exec: a city without its order is a problem", ExecutionChecks.VerifyFinal(missingFinal, placedPlan, demand, signatures).Any(p => p.Contains("zarnowiec: no order")), string.Join("; ", ExecutionChecks.VerifyFinal(missingFinal, placedPlan, demand, signatures)));
        Expect("exec: a city without a signature is a problem", ExecutionChecks.VerifyFinal(placed, placedPlan, demand, new Dictionary<string, string> { ["komarowo"] = "9fde" }).Any(p => p.Contains("zarnowiec: no signature")));

        // --- report ----------------------------------------------------------------------------------------
        var failed = results.Where(r => !r.Passed).ToList();
        foreach (var (name, passed, detail) in results)
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {name}{(passed || detail.Length == 0 ? string.Empty : $"  -- {detail}")}");

        Console.WriteLine();
        Console.WriteLine($"{results.Count - failed.Count}/{results.Count} passed.");
        return failed.Count == 0;
    }
}
