using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace _02_01_zadanie.Categorize;

public sealed record RenderedPrompt(CatalogItem Item, string Prompt, int Tokens);

/// <summary>
/// Everything the tooling knows about a template before any budget is spent on it: validation
/// problems, per-item token counts against the current catalog, and PP estimates for the batch.
/// </summary>
public sealed class TemplateAnalysis
{
    public required string Template { get; init; }
    public required IReadOnlyList<string> Problems { get; init; }
    public required IReadOnlyList<RenderedPrompt> Rendered { get; init; }
    public required int PrefixTokens { get; init; }
    public required int EffectiveTokenLimit { get; init; }
    public required double EstimatedCostNoCachePp { get; init; }
    public required double EstimatedCostWithCachePp { get; init; }
    public required double BudgetPp { get; init; }

    public bool IsValid => Problems.Count == 0;
    public int MaxPromptTokens => Rendered.Count == 0 ? 0 : Rendered.Max(r => r.Tokens);

    public JsonObject ToJsonObject()
    {
        var problems = new JsonArray();
        foreach (var problem in Problems)
            problems.Add(problem);

        var items = new JsonArray();
        foreach (var rendered in Rendered)
        {
            items.Add(new JsonObject
            {
                ["id"]           = rendered.Item.Id,
                ["description"]  = rendered.Item.Description,
                ["promptTokens"] = rendered.Tokens
            });
        }

        var json = new JsonObject
        {
            ["valid"]                         = IsValid,
            ["problems"]                      = problems,
            ["prefixTokensCacheable"]         = PrefixTokens,
            ["maxPromptTokens"]               = MaxPromptTokens,
            ["effectiveTokenLimit"]           = EffectiveTokenLimit,
            ["estimatedBatchCostNoCachePp"]   = Math.Round(EstimatedCostNoCachePp, 3),
            ["estimatedBatchCostWithCachePp"] = Math.Round(EstimatedCostWithCachePp, 3),
            ["budgetPp"]                      = BudgetPp,
            ["catalogItems"]                  = items
        };

        if (Rendered.Count > 0)
            json["exampleRenderedPrompt"] = Rendered[0].Prompt;

        return json;
    }
}

public static class TemplateAnalyzer
{
    public const string IdPlaceholder = "{id}";
    public const string DescriptionPlaceholder = "{description}";

    // PP prices from the task: 0.02 PP per 10 input tokens, 0.01 PP per 10 cached, 0.02 PP per 10 output.
    private const double InputPpPerToken = 0.002;
    private const double CachedInputPpPerToken = 0.001;
    private const double OutputPpPerToken = 0.002;
    private const int EstimatedOutputTokensPerItem = 2;

    public static TemplateAnalysis Analyze(string template, IReadOnlyList<CatalogItem> items, TokenCounter tokens, CategorizeOptions options)
    {
        var problems = new List<string>();
        var effectiveLimit = options.TokenLimit - options.TokenSafetyMargin;

        foreach (var placeholder in UnknownPlaceholders(template))
            problems.Add($"Unknown placeholder {placeholder} — only {IdPlaceholder} and {DescriptionPlaceholder} are substituted; anything else in braces would reach the classifier literally.");

        var idCount = CountOccurrences(template, IdPlaceholder);
        var descriptionCount = CountOccurrences(template, DescriptionPlaceholder);

        if (idCount != 1)
            problems.Add($"The template must contain {IdPlaceholder} exactly once (found {idCount}); the remote system reads the item id from the prompt to know which item is being classified.");
        if (descriptionCount != 1)
            problems.Add($"The template must contain {DescriptionPlaceholder} exactly once (found {descriptionCount}).");

        if (items.Count == 0)
            problems.Add("The downloaded catalog parsed into 0 items, so the template cannot be measured. This is a catalog problem, not a template problem — report it instead of guessing.");

        var rendered = new List<RenderedPrompt>();
        var prefixTokens = 0;
        double costNoCache = 0;
        double costWithCache = 0;

        if (idCount == 1 && descriptionCount == 1 && items.Count > 0)
        {
            var firstPlaceholderIndex = Math.Min(
                template.IndexOf(IdPlaceholder, StringComparison.Ordinal),
                template.IndexOf(DescriptionPlaceholder, StringComparison.Ordinal));
            prefixTokens = tokens.Count(template[..firstPlaceholderIndex]);

            foreach (var item in items)
            {
                var prompt = Render(template, item);
                var count = tokens.Count(prompt);
                rendered.Add(new RenderedPrompt(item, prompt, count));

                if (count > effectiveLimit)
                    problems.Add($"Rendered prompt for item '{item.Id}' is {count} tokens — over the effective limit of {effectiveLimit} ({options.TokenLimit}-token window minus the {options.TokenSafetyMargin}-token margin for tokenizer differences).");
            }

            var outputCost = rendered.Count * EstimatedOutputTokensPerItem * OutputPpPerToken;
            costNoCache = rendered.Sum(r => r.Tokens) * InputPpPerToken + outputCost;
            costWithCache = rendered[0].Tokens * InputPpPerToken
                + rendered.Skip(1).Sum(r => prefixTokens * CachedInputPpPerToken + (r.Tokens - prefixTokens) * InputPpPerToken)
                + outputCost;

            if (costWithCache > options.BudgetPp)
                problems.Add($"Even with a fully cached prefix the batch would cost ~{costWithCache:0.###} PP, over the {options.BudgetPp} PP budget — the template must get shorter.");
        }

        return new TemplateAnalysis
        {
            Template                 = template,
            Problems                 = problems,
            Rendered                 = rendered,
            PrefixTokens             = prefixTokens,
            EffectiveTokenLimit      = effectiveLimit,
            EstimatedCostNoCachePp   = costNoCache,
            EstimatedCostWithCachePp = costWithCache,
            BudgetPp                 = options.BudgetPp
        };
    }

    public static string Render(string template, CatalogItem item) =>
        template.Replace(IdPlaceholder, item.Id).Replace(DescriptionPlaceholder, item.Description);

    private static IEnumerable<string> UnknownPlaceholders(string template) =>
        Regex.Matches(template, @"\{[^{}]*\}")
            .Select(match => match.Value)
            .Where(value => value is not (IdPlaceholder or DescriptionPlaceholder))
            .Distinct();

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
