namespace _02_01_zadanie.Categorize;

/// <summary>Runtime settings for the categorize task, bound from the "Categorize" configuration section.</summary>
public sealed class CategorizeOptions
{
    public string VerifyUrl { get; init; } = "https://hub.ag3nts.org/verify";

    /// <summary>Catalog URL with the API key already substituted in.</summary>
    public string CsvUrl { get; init; } = string.Empty;

    public string TaskName { get; init; } = "categorize";

    /// <summary>Context window of the remote classifier, in its own tokens.</summary>
    public int TokenLimit { get; init; } = 100;

    /// <summary>
    /// Tokens held back from the limit because the local o200k_base count only approximates
    /// the remote tokenizer ("counted roughly like GPT-5.2").
    /// </summary>
    public int TokenSafetyMargin { get; init; } = 8;

    /// <summary>Total PP budget for the whole batch of submissions.</summary>
    public double BudgetPp { get; init; } = 1.5;

    public TimeSpan MinInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    public int MaxAttemptsPerCall { get; init; } = 6;
    public string? LogPath { get; init; }
}
