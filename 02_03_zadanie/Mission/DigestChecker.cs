using System.Text;

namespace _02_03_zadanie.Mission;

/// <summary>
/// The complete local pre-flight of a digest: token budget plus validation against the source.
/// Shared by the check and submit tools and by the offline --check / --submit modes, so every
/// path applies exactly the same rules to exactly the same normalised text.
/// </summary>
public sealed class DigestChecker(DigestValidator validator, TokenBudget budget)
{
    public DigestCheckResult Check(string digest)
    {
        var normalized = digest.Replace("\r\n", "\n").Trim('\n');
        return new DigestCheckResult(normalized, budget.Check(normalized), validator.Validate(normalized));
    }
}

public sealed record DigestCheckResult(string Digest, BudgetCheck Budget, IReadOnlyList<string> Problems)
{
    public int LineCount => Digest.Split('\n').Length;

    public bool Passed => Budget.Fits && Problems.Count == 0;

    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Lines: {LineCount}. {Budget.Describe()}");
        if (Problems.Count == 0)
            sb.AppendLine("Format: OK, every line matches a real source entry.");
        else
        {
            sb.AppendLine($"Format: {Problems.Count} problem(s):");
            foreach (var problem in Problems)
                sb.AppendLine($"  - {problem}");
        }
        return sb.ToString();
    }
}
