using System.Text.Json.Nodes;

namespace _04_03_zadanie.Mission;

/// <summary>
/// The price of every action. The task text lists the prices too, but the table is read from actionCost
/// whenever a reply is at hand, so a hub that changes its tariff changes the plan instead of breaking it.
/// </summary>
public sealed record CostTable(int ScoutCreate, int TransporterBase, int TransporterPerPassenger, int ScoutMovePerField, int TransporterMovePerField, int Inspect, int Dismount, int CallHelicopter)
{
    /// <summary>The prices from the task text, used only until actionCost has been read.</summary>
    public static CostTable FromTaskText { get; } = new(5, 5, 5, 7, 1, 1, 0, 0);

    public int Transporter(int passengers) => TransporterBase + passengers * TransporterPerPassenger;

    public int ScoutMove(int steps) => steps * ScoutMovePerField;

    public int TransporterMove(int steps) => steps * TransporterMovePerField;

    public static CostTable Load(string path) => Parse(File.ReadAllText(path));

    public static CostTable Parse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("actionCost reply is not an object.");
        var entries = root["costs"] as JsonArray ?? throw new FormatException("actionCost reply has no costs array.");

        int? scoutCreate = null, transporterBase = null, perPassenger = null, scoutMove = null, transporterMove = null, inspect = null;
        var dismount = 0;
        var helicopter = 0;

        foreach (var entry in entries.OfType<JsonObject>())
        {
            var action = entry["action"]?.GetValue<string>();
            var variant = entry["variant"]?.GetValue<string>();

            switch (action, variant)
            {
                case ("create", "scout"):
                    scoutCreate = Read(entry, "cost");
                    break;
                case ("create", "transporter"):
                    transporterBase = Read(entry, "base_cost");
                    perPassenger = Read(entry, "cost_per_passenger");
                    break;
                case ("move", "scout"):
                    scoutMove = Read(entry, "cost_per_field");
                    break;
                case ("move", "transporter"):
                    transporterMove = Read(entry, "cost_per_field");
                    break;
                case ("inspect", _):
                    inspect = Read(entry, "cost");
                    break;
                case ("dismount", _):
                    dismount = Read(entry, "cost");
                    break;
                case ("callHelicopter", _):
                    helicopter = Read(entry, "cost");
                    break;
            }
        }

        return new CostTable(
            scoutCreate ?? throw Missing("create/scout"),
            transporterBase ?? throw Missing("create/transporter"),
            perPassenger ?? throw Missing("create/transporter per passenger"),
            scoutMove ?? throw Missing("move/scout"),
            transporterMove ?? throw Missing("move/transporter"),
            inspect ?? throw Missing("inspect"),
            dismount,
            helicopter);
    }

    private static int Read(JsonObject entry, string field) =>
        entry[field]?.GetValue<int>() ?? throw new FormatException($"actionCost entry '{entry["action"]}' has no '{field}'.");

    private static FormatException Missing(string what) => new($"actionCost lists no price for {what}.");

    public override string ToString() =>
        $"scout {ScoutCreate}, transporter {TransporterBase}+{TransporterPerPassenger}/passenger, move scout {ScoutMovePerField}/field, move transporter {TransporterMovePerField}/field, inspect {Inspect}, dismount {Dismount}, helicopter {CallHelicopter}";
}
