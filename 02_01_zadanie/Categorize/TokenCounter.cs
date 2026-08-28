using Microsoft.ML.Tokenizers;

namespace _02_01_zadanie.Categorize;

/// <summary>
/// Local token counting with the o200k_base encoding. The hub only says its tokens are counted
/// "roughly like GPT-5.2", so every count is an approximation — callers keep a safety margin.
/// </summary>
public sealed class TokenCounter
{
    private readonly TiktokenTokenizer _tokenizer = TiktokenTokenizer.CreateForEncoding("o200k_base");

    public int Count(string text) => _tokenizer.CountTokens(text);
}
