using System.Text.Json;
using _04_05_zadanie.Hub;
using _04_05_zadanie.Mission;
using _04_05_zadanie.Warehouse;

namespace _04_05_zadanie.Tools;

/// <summary>
/// The agent's window on the database. The guard answers before the hub does, every row that
/// comes back is recorded as an observation, and an identical query is served from memory rather
/// than sent twice.
/// </summary>
public sealed class QueryDatabaseTool(MissionState state, FoodwarehouseClient hub) : ITool
{
    public const string ToolName = "query_database";

    private readonly Dictionary<string, string> _answered = new(StringComparer.Ordinal);

    public string Name => ToolName;

    public string Description =>
        "Runs one read-only query against the warehouse's SQLite database: SELECT ..., SHOW TABLES, SHOW CREATE TABLE <name>, .tables, .schema or .schema <name>. " +
        "Results come in pages of 30 rows; the result says when a page may continue and how to fetch the next one. " +
        "Rows are recorded as observations only under their original column names (destination_id, name, user_id, login, birthday, role, is_active), so do not alias them. " +
        "The hub rejects '<' and '>' anywhere in a query; write != instead of <>.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "The query text, one statement, no trailing semicolon." }
          },
          "required": ["query"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var query = ToolArguments.Parse(argumentsJson).GetString("query");
        var verdict = QueryGuard.Evaluate(query);
        if (!verdict.Allowed)
        {
            state.CountQuery(refused: true);
            return $"Refused (no request was sent): {verdict.Reason}";
        }

        if (_answered.TryGetValue(verdict.Query, out var earlier))
            return $"(This exact query was already run; its result is repeated, no request was sent.){Environment.NewLine}{earlier}";

        var reply = await hub.QueryAsync(verdict.Query, cancellationToken);
        state.CountQuery(refused: false);

        if (!reply.IsSuccess)
            return $"The database refused the query: {reply.Describe()}";

        var result = QueryResult.Parse(reply.Body);
        var summary = state.Observations.Absorb(result);
        // A reply of a shape the parser does not know is shown raw; an empty SELECT is a normal, rendered result.
        var rendered = result.Table is null && result.Rows.Count == 0 && result.Schemas.Count == 0 && result.Columns.Count == 0
            ? reply.Body.Trim()
            : result.Render();

        var answer = $"{rendered}{Environment.NewLine}{summary.Render()}";
        _answered[verdict.Query] = answer;
        return answer;
    }
}
