using System.Text.Json;
using _04_05_zadanie.Hub;
using _04_05_zadanie.Mission;
using _04_05_zadanie.Warehouse;

namespace _04_05_zadanie.Tools;

/// <summary>
/// The orders as the warehouse holds them right now. They are the only example of an accepted
/// order the agent gets, which is why the plan cannot be registered before they have been seen.
/// </summary>
public sealed class ListOrdersTool(MissionState state, FoodwarehouseClient hub) : ITool
{
    public const string ToolName = "list_orders";

    public string Name => ToolName;

    public string Description =>
        "Lists the orders currently stored in the warehouse: id, title, creatorID, destination, signature and items. " +
        "Existing orders show what the warehouse accepts. This does not change anything.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var reply = await hub.GetOrdersAsync(cancellationToken: cancellationToken);
        if (!reply.IsSuccess)
            return $"The warehouse refused to list the orders: {reply.Describe()}";

        var orders = OrderBook.Parse(reply.Body);
        state.Observations.AbsorbOrders(orders);
        return OrderBook.Render(orders);
    }
}
