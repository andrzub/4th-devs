namespace _05_02_zadanie.Call;

public enum LineKind
{
    Introduction,
    RoadStatusQuestion,
    Password,
    Justification,
    DisableMonitoring,

    /// <summary>Written by the model during the call, for a question the script does not answer.</summary>
    Improvised
}

/// <summary>One thing the caller can say: a stable key in the recording bank, the spoken text and the terms a listener must hear in it.</summary>
public sealed record ScriptLine(string Key, LineKind Kind, string Text, IReadOnlyList<string> RequiredTerms);
