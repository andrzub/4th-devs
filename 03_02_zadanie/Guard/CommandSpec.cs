namespace _03_02_zadanie.Guard;

/// <summary>
/// The documented command set of the virtual machine, taken from its own <c>help</c>. Knowing the
/// arity and the meaning of each argument is what lets the guard check paths without mistaking a
/// settings value for one; an undocumented verb is refused before it costs a request.
/// </summary>
public sealed record CommandSpec(string Verb, int MinArguments, int MaxArguments, IReadOnlyList<ArgumentKind> ArgumentKinds, string Usage)
{
    public ArgumentKind KindAt(int index) => index < ArgumentKinds.Count ? ArgumentKinds[index] : ArgumentKinds[^1];

    private static readonly CommandSpec[] All =
    [
        new("help",     0, 0, [ArgumentKind.FreeText],    "help"),
        new("ls",       0, 1, [ArgumentKind.Path],        "ls [path]"),
        new("cat",      1, 1, [ArgumentKind.Path],        "cat <path>"),
        new("cd",       0, 1, [ArgumentKind.Path],        "cd [path]"),
        new("pwd",      0, 0, [ArgumentKind.FreeText],    "pwd"),
        new("rm",       1, 1, [ArgumentKind.Path],        "rm <file>"),
        new("editline", 3, 3, [ArgumentKind.Path, ArgumentKind.Number, ArgumentKind.FreeText], "editline <file> <line-number> <content>"),
        new("reboot",   0, 0, [ArgumentKind.FreeText],    "reboot"),
        new("date",     0, 0, [ArgumentKind.FreeText],    "date"),
        new("uptime",   0, 0, [ArgumentKind.FreeText],    "uptime"),
        new("find",     1, 1, [ArgumentKind.NamePattern], "find <pattern>"),
        new("history",  0, 0, [ArgumentKind.FreeText],    "history"),
        new("whoami",   0, 0, [ArgumentKind.FreeText],    "whoami")
    ];

    public static IReadOnlyList<CommandSpec> Known => All;

    public static CommandSpec? Find(string verb) => All.FirstOrDefault(s => string.Equals(s.Verb, verb, StringComparison.Ordinal));

    public static string VerbList => string.Join(", ", All.Select(s => s.Verb));
}
