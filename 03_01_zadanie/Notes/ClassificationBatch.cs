using System.Text;

namespace _03_01_zadanie.Notes;

public sealed record ClassificationItem(string Text, NoteTone? Expected);

/// <summary>
/// One batch of statements on its way to the model, with two control statements of known
/// verdict shuffled in among them. The controls are the guard against a reply that flags
/// nothing because the model stopped reading rather than because everything was routine —
/// a failure mode that is otherwise indistinguishable from success.
/// </summary>
public sealed class ClassificationBatch
{
    /// <summary>
    /// Deliberately not drawn from the plant's own vocabulary, so a control statement can never
    /// collide with a real note and can never be mistaken for one in the logs.
    /// </summary>
    public static readonly (string Text, NoteTone Expected)[] Controls =
    [
        ("the coolant pump is leaking and the unit must be stopped for repair", NoteTone.Problem),
        ("all instruments read normally and no action is needed", NoteTone.Ok)
    ];

    private ClassificationBatch(IReadOnlyList<ClassificationItem> items, int statementCount)
    {
        Items = items;
        StatementCount = statementCount;
    }

    public IReadOnlyList<ClassificationItem> Items { get; }

    /// <summary>How many real statements the batch carries, controls excluded.</summary>
    public int StatementCount { get; }

    /// <summary>
    /// Builds the batch, placing the controls at positions derived from <paramref name="seed"/>
    /// so a re-run plants them identically and its log stays comparable with the previous one.
    /// </summary>
    public static ClassificationBatch Create(IReadOnlyList<string> statements, int seed)
    {
        var items = statements.Select(text => new ClassificationItem(text, null)).ToList();
        var random = new Random(seed);

        foreach (var (text, expected) in Controls)
        {
            if (statements.Contains(text, StringComparer.Ordinal))
                continue;

            items.Insert(random.Next(items.Count + 1), new ClassificationItem(text, expected));
        }

        return new ClassificationBatch(items, statements.Count);
    }

    public string RenderPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Classify the following {Items.Count} statements.");
        sb.AppendLine();

        for (var i = 0; i < Items.Count; i++)
            sb.AppendLine($"{i + 1}. {Items[i].Text}");

        return sb.ToString();
    }

    /// <summary>Returns why the reply must be rejected, or null when it can be trusted.</summary>
    public string? Verify(ClassificationReply reply)
    {
        if (reply.Error is not null)
            return reply.Error;

        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Expected is not { } expected)
                continue;

            var actual = reply.ToneOf(i + 1);
            if (actual != expected)
                return $"control statement at position {i + 1} came back as {actual} instead of {expected}";
        }

        return null;
    }

    public Dictionary<string, NoteTone> CollectVerdicts(ClassificationReply reply)
    {
        var verdicts = new Dictionary<string, NoteTone>(StringComparer.Ordinal);

        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Expected is null)
                verdicts[Items[i].Text] = reply.ToneOf(i + 1);
        }

        return verdicts;
    }
}
