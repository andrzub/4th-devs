namespace _03_01_zadanie.Notes;

/// <summary>
/// What an operator note asserts about the reading it describes. This is the only question the
/// language model is asked in this task.
/// </summary>
public enum NoteTone
{
    /// <summary>The note asserts the reading is healthy and needs no action.</summary>
    Ok,

    /// <summary>The note asserts something is wrong, suspicious, or needs follow-up.</summary>
    Problem,

    /// <summary>Neither — filler text, or a genuinely ambiguous statement.</summary>
    Unclear
}
