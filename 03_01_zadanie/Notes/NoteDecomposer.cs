namespace _03_01_zadanie.Notes;

/// <summary>
/// Splits an operator note into the independent clauses it was assembled from. The plant's
/// notes turn out to be templated — a handful of clause pools joined with commas — so judging
/// clauses instead of whole notes collapses the work the model has to do by roughly six times.
/// The structure is discovered here at runtime rather than hard-coded, and notes that do not
/// decompose are classified whole.
/// </summary>
public static class NoteDecomposer
{
    private const int MinClausesToDecompose = 2;

    public static IReadOnlyList<string> Split(string note)
    {
        var clauses = note
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(clause => clause.TrimEnd('.').Trim())
            .Where(clause => clause.Length > 0)
            .ToList();

        return clauses.Count >= MinClausesToDecompose ? clauses : new List<string> { note.Trim() };
    }

    public static bool Decomposes(string note) => Split(note).Count > 1;

    /// <summary>
    /// Folds clause verdicts into a verdict for the whole note. A note is only healthy when
    /// every one of its clauses is, so a single clause claiming trouble decides the note —
    /// which is also what keeps clause-level judgement safe when a note mixes both tones.
    /// </summary>
    public static NoteTone Combine(IEnumerable<NoteTone> clauseTones)
    {
        var tones = clauseTones.ToList();

        if (tones.Count == 0)
            return NoteTone.Unclear;
        if (tones.Contains(NoteTone.Problem))
            return NoteTone.Problem;
        if (tones.Contains(NoteTone.Ok))
            return NoteTone.Ok;

        return NoteTone.Unclear;
    }
}
