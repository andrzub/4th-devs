using _02_03_zadanie.Analysis;

namespace _02_03_zadanie.Mission;

/// <summary>
/// The hub's hard token limit plus a local safety margin. The hub does not disclose its exact
/// tokenizer, so a digest is only allowed out when it fits under the safe limit.
/// </summary>
public sealed class TokenBudget
{
    private readonly TokenCounter _counter;

    public TokenBudget(TokenCounter counter, int hardLimit, int safetyMargin)
    {
        _counter = counter;
        HardLimit = hardLimit;
        SafeLimit = hardLimit - safetyMargin;
    }

    public int HardLimit { get; }
    public int SafeLimit { get; }

    public BudgetCheck Check(string text) => new(_counter.Count(text), HardLimit, SafeLimit);
}

public readonly record struct BudgetCheck(int Tokens, int HardLimit, int SafeLimit)
{
    public bool Fits => Tokens <= SafeLimit;

    public string Describe() => Fits
        ? $"{Tokens} tokens (o200k_base): fits, {SafeLimit - Tokens} tokens of headroom under the safe limit {SafeLimit} (hard limit {HardLimit})."
        : $"{Tokens} tokens (o200k_base): TOO LONG by {Tokens - SafeLimit} tokens. Safe limit is {SafeLimit} (hard limit {HardLimit}). Shorten wording or drop lines.";
}
