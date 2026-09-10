using System.Text;
using _03_01_zadanie.Llm;

namespace _03_01_zadanie.Observability;

public sealed class TraceUsage
{
    public int Calls { get; internal set; }
    public int PromptTokens { get; internal set; }
    public int CachedPromptTokens { get; internal set; }
    public int CompletionTokens { get; internal set; }
    public decimal Cost { get; internal set; }
    public int ItemsClassified { get; internal set; }

    public decimal CostPerThousandItems => ItemsClassified == 0 ? 0 : Cost * 1000 / ItemsClassified;
}

/// <summary>
/// Token and money accounting for the whole run, grouped the way the observability platforms
/// group it: per trace, then rolled up. Prices come from configuration, so switching models
/// or tariffs does not touch code.
/// </summary>
public sealed class UsageMeter(LlmProviderSettings pricing)
{
    private readonly Dictionary<string, TraceUsage> _byTrace = new();
    private readonly object _lock = new();

    public IReadOnlyDictionary<string, TraceUsage> ByTrace => _byTrace;

    public decimal CostOf(LlmUsage usage)
    {
        var freshPromptTokens = Math.Max(0, usage.PromptTokens - usage.CachedPromptTokens);

        return freshPromptTokens * pricing.InputPricePerMillionTokens / 1_000_000m
             + usage.CachedPromptTokens * pricing.CachedInputPricePerMillionTokens / 1_000_000m
             + usage.CompletionTokens * pricing.OutputPricePerMillionTokens / 1_000_000m;
    }

    public decimal Record(string trace, LlmUsage usage, int itemsClassified)
    {
        var cost = CostOf(usage);

        lock (_lock)
        {
            if (!_byTrace.TryGetValue(trace, out var entry))
                _byTrace[trace] = entry = new TraceUsage();

            entry.Calls++;
            entry.PromptTokens += usage.PromptTokens;
            entry.CachedPromptTokens += usage.CachedPromptTokens;
            entry.CompletionTokens += usage.CompletionTokens;
            entry.ItemsClassified += itemsClassified;
            entry.Cost += cost;
        }

        return cost;
    }

    public TraceUsage Total()
    {
        lock (_lock)
        {
            var total = new TraceUsage();
            foreach (var entry in _byTrace.Values)
            {
                total.Calls += entry.Calls;
                total.PromptTokens += entry.PromptTokens;
                total.CachedPromptTokens += entry.CachedPromptTokens;
                total.CompletionTokens += entry.CompletionTokens;
                total.ItemsClassified += entry.ItemsClassified;
                total.Cost += entry.Cost;
            }
            return total;
        }
    }

    public string RenderTable()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  {"trace",-26} {"calls",5} {"items",7} {"in",9} {"cached",8} {"out",7} {"USD",11}");

        lock (_lock)
        {
            foreach (var (trace, usage) in _byTrace.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                sb.AppendLine(Row(trace, usage));
        }

        var total = Total();
        sb.AppendLine($"  {new string('-', 76)}");
        sb.AppendLine(Row("TOTAL", total));

        if (total.ItemsClassified > 0)
            sb.AppendLine($"  cost per 1000 classified items: ${total.CostPerThousandItems:0.0000}");

        return sb.ToString();

        static string Row(string label, TraceUsage usage) =>
            $"  {label,-26} {usage.Calls,5} {usage.ItemsClassified,7} {usage.PromptTokens,9:N0} {usage.CachedPromptTokens,8:N0} {usage.CompletionTokens,7:N0} ${usage.Cost,10:0.000000}";
    }
}
