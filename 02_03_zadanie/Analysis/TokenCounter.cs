using Microsoft.ML.Tokenizers;

namespace _02_03_zadanie.Analysis;

/// <summary>
/// Local token counting with the o200k_base encoding. The hub's tokenizer is not specified
/// exactly, so every count is an approximation and callers keep a safety margin.
/// </summary>
public sealed class TokenCounter
{
    private readonly TiktokenTokenizer _tokenizer = TiktokenTokenizer.CreateForEncoding("o200k_base");

    public int Count(string text) => _tokenizer.CountTokens(text);
}
