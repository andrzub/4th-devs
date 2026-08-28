using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _02_01_zadanie.Categorize;

namespace _02_01_zadanie.Tools;

/// <summary>
/// The only tool that spends budget. One call = one full remote cycle: reset the remote counter,
/// download a fresh catalog, render the template per item and submit the prompts one by one.
/// A template that fails local validation is refused before anything is sent, and hub responses
/// are handed back raw so the model reads the hub's own wording rather than a paraphrase.
/// </summary>
public sealed class RunClassificationCycleTool : ITool
{
    private static readonly Regex FlagPattern = new(@"\{\{?FLG:[^}]+\}\}?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HubClient _hub;
    private readonly TokenCounter _tokens;
    private readonly CategorizeOptions _options;

    public RunClassificationCycleTool(HubClient hub, TokenCounter tokens, CategorizeOptions options)
    {
        _hub = hub;
        _tokens = tokens;
        _options = options;
    }

    /// <summary>Set once a response carried a flag, so the loop can stop.</summary>
    public bool FlagFound { get; private set; }

    /// <summary>The flag exactly as the hub returned it.</summary>
    public string? Flag { get; private set; }

    public int CyclesRun { get; private set; }

    public string Name => "run_classification_cycle";

    public string Description =>
        "Run one full, budget-spending classification cycle: reset the remote token counter, download a fresh " +
        "catalog, render the template for each item and submit the prompts one by one. Refuses for free when " +
        "the template fails local validation. Returns the raw remote response for every submission — read them " +
        "literally. Transport errors and throttling are retried automatically, so a result you see is final; " +
        "never rerun an unchanged template hoping for a different outcome.";

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

        var analysis = TemplateAnalyzer.Analyze(template, CsvCatalog.Parse(csv), _tokens, _options);
        var report = analysis.ToJsonObject();

        if (!analysis.IsValid)
        {
            report["refused"] = "Template failed local validation — nothing was sent and no budget was spent. Fix the listed problems.";
            return report.ToJsonString();
        }

        CyclesRun++;
        Console.WriteLine($"  [cycle] prefix {analysis.PrefixTokens} tok | max prompt {analysis.MaxPromptTokens} tok | est. {analysis.EstimatedCostWithCachePp:0.###} PP with cache");

        HubResponse reset;
        try
        {
            reset = await _hub.SendPromptAsync("reset", cancellationToken);
        }
        catch (Exception ex)
        {
            return $"The reset call could not be completed even after retrying: {_hub.Redact(ex.Message)}";
        }

        Console.WriteLine($"  [cycle] reset: HTTP {reset.StatusCode} {Preview(reset.Body)}");
        report["reset"] = new JsonObject { ["status"] = reset.StatusCode, ["body"] = reset.Body };
        CaptureFlag(reset.Body);

        var submissions = new JsonArray();

        foreach (var rendered in analysis.Rendered)
        {
            if (FlagFound)
                break;

            HubResponse response;
            try
            {
                response = await _hub.SendPromptAsync(rendered.Prompt, cancellationToken);
            }
            catch (Exception ex)
            {
                submissions.Add(new JsonObject { ["id"] = rendered.Item.Id, ["error"] = _hub.Redact(ex.Message) });
                break;
            }

            Console.WriteLine($"  [cycle] {rendered.Item.Id}: HTTP {response.StatusCode} {Preview(response.Body)}");
            submissions.Add(new JsonObject { ["id"] = rendered.Item.Id, ["status"] = response.StatusCode, ["body"] = response.Body });
            CaptureFlag(response.Body);

            if (FlagFound)
                break;

            var code = TryReadCode(response.Body);

            // A misclassification zeroes the remaining balance as a penalty, so everything after
            // it would be a -910 refusal that carries no information — stop the cycle right here.
            if (code == -890)
            {
                report["stoppedEarly"] = "A misclassification (code -890) instantly zeroes the remaining balance — every further submission this cycle would fail with -910 and carry no information. The problem is the template's ACCURACY on the item above, not its cost.";
                break;
            }

            if (code == -910)
            {
                report["stoppedEarly"] = "The balance ran out (code -910) with no misclassification before it — the template really is too expensive; shorten it or enlarge its cacheable prefix.";
                break;
            }
        }

        report["submissions"] = submissions;
        report["flag"] = Flag;

        if (FlagFound)
            report["done"] = "A flag was received — the task is complete. Stop calling tools and summarise.";

        return report.ToJsonString();
    }

    private void CaptureFlag(string body)
    {
        var match = FlagPattern.Match(body);
        if (!match.Success)
            return;

        FlagFound = true;
        Flag = match.Value;

        Console.WriteLine();
        Console.WriteLine($"=== FLAGA: {match.Value} ===");
        Console.WriteLine();
    }

    private static int? TryReadCode(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["code"]?.GetValue<int>();
        }
        catch
        {
            return null;
        }
    }

    private static string Preview(string text)
    {
        var flattened = text.ReplaceLineEndings(" ");
        return flattened.Length > 160 ? flattened[..160] + "…" : flattened;
    }
}
