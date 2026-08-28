using System.Text.Json;
using _02_01_zadanie.Categorize;

namespace _02_01_zadanie.Tools;

/// <summary>
/// Free, local template check: measures the template against the current catalog without sending
/// anything to /verify, so the agent can iterate on wording and token counts at zero PP cost.
/// </summary>
public sealed class ValidateTemplateTool : ITool
{
    private readonly HubClient _hub;
    private readonly TokenCounter _tokens;
    private readonly CategorizeOptions _options;

    public ValidateTemplateTool(HubClient hub, TokenCounter tokens, CategorizeOptions options)
    {
        _hub = hub;
        _tokens = tokens;
        _options = options;
    }

    public string Name => "validate_template";

    public string Description =>
        "Free local check of a prompt template — nothing is sent to the classifier and no budget is spent. " +
        "Downloads the current catalog, renders the template for every item, measures token counts against " +
        "the window limit and estimates the batch cost in PP with and without prefix caching. The template " +
        "must contain the placeholders {id} and {description}, each exactly once. Use this before every " +
        "run_classification_cycle.";

    public JsonElement ParametersSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "template": {
              "type": "string",
              "description": "The classification prompt template, with {id} and {description} placeholders."
            }
          },
          "required": ["template"],
          "additionalProperties": false
        }
        """);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        if (ToolArguments.ReadTemplate(argumentsJson) is not { } template)
            return ToolArguments.TemplateError;

        string csv;
        try
        {
            csv = await _hub.FetchCatalogCsvAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Could not download the catalog even after retrying: {_hub.Redact(ex.Message)}";
        }

        var report = TemplateAnalyzer.Analyze(template, CsvCatalog.Parse(csv), _tokens, _options).ToJsonObject();
        report["note"] = "Local check only — nothing was sent to the classifier.";
        return report.ToJsonString();
    }
}
