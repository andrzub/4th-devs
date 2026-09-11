namespace _03_02_zadanie.Guard;

/// <summary>
/// How one argument of a command is meant to be read. The distinction matters because the
/// guard resolves and checks paths, leaves name patterns alone, and must not touch free text
/// at all: the content of an <c>editline</c> is a settings value, not a location.
/// </summary>
public enum ArgumentKind
{
    Path,
    NamePattern,
    Number,
    FreeText
}

public sealed record CommandArgument(string Value, ArgumentKind Kind);

/// <summary>
/// A command parsed into the shape the virtual machine's dispatcher expects. The verb is either
/// one of the documented commands or an absolute path, which is how a binary gets executed here.
/// </summary>
public sealed record ShellCommand(string Raw, string Verb, IReadOnlyList<CommandArgument> Arguments)
{
    public bool IsExecutable => Verb.StartsWith('/');

    public IEnumerable<CommandArgument> PathArguments => Arguments.Where(a => a.Kind == ArgumentKind.Path);
}
