namespace _03_01_zadanie.Notes;

public enum ClassificationTier
{
    /// <summary>Judge each clause a note was assembled from, then fold the verdicts together.</summary>
    Clauses,

    /// <summary>Judge whole notes, deduplicated.</summary>
    Notes
}

/// <summary>
/// Turns a set of operator notes into a verdict per note, at the chosen granularity. The
/// clause tier exists because the plant's notes are templated: judging the clause pool costs a
/// fraction of judging every distinct note, and a note is only healthy when all of its clauses
/// are. Notes that do not decompose travel through the same pass as whole statements, so a
/// hand-written note is never chopped into fragments that lost their meaning.
/// </summary>
public sealed class NoteToneResolver(NoteToneClassifier classifier)
{
    public async Task<IReadOnlyDictionary<string, NoteTone>> ResolveAsync(IReadOnlyCollection<string> notes, ClassificationTier tier, CancellationToken cancellationToken = default)
    {
        var distinctNotes = notes.Distinct(StringComparer.Ordinal).ToList();

        if (tier == ClassificationTier.Notes)
            return await classifier.ClassifyAsync(distinctNotes, "classify:notes", cancellationToken);

        var clausesByNote = distinctNotes.ToDictionary(note => note, NoteDecomposer.Split, StringComparer.Ordinal);
        var statements = clausesByNote.Values.SelectMany(clauses => clauses).Distinct(StringComparer.Ordinal).ToList();

        var clauseTones = await classifier.ClassifyAsync(statements, "classify:clauses", cancellationToken);

        return distinctNotes.ToDictionary(
            note => note,
            note => NoteDecomposer.Combine(clausesByNote[note].Select(clause => clauseTones[clause])),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// How many statements each tier would have to classify. Printed before any money is spent
    /// so the two options can be compared on the actual data rather than on a guess.
    /// </summary>
    public static (int Notes, int Statements) CountWork(IReadOnlyCollection<string> notes)
    {
        var distinctNotes = notes.Distinct(StringComparer.Ordinal).ToList();
        var statements = distinctNotes.SelectMany(NoteDecomposer.Split).Distinct(StringComparer.Ordinal).Count();

        return (distinctNotes.Count, statements);
    }
}
