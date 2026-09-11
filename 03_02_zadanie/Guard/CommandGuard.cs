namespace _03_02_zadanie.Guard;

public sealed record GuardDecision(bool IsAllowed, string Reason, ShellCommand? Command)
{
    public static GuardDecision Deny(string reason) => new(false, reason, null);
    public static GuardDecision Allow(ShellCommand command) => new(true, "Allowed.", command);
}

/// <summary>
/// The last thing between the agent and a command that would get the run banned. The machine
/// answers a violation by cutting access and rebuilding itself, which throws away every finding
/// so far, so the blacklist is enforced by code rather than trusted to the prompt: a refusal here
/// costs one turn, while a refusal by the machine costs the whole run.
/// </summary>
/// <param name="forbiddenRoots">Directories the task puts out of bounds, from configuration.</param>
public sealed class CommandGuard(IReadOnlyList<string> forbiddenRoots)
{
    private static readonly char[] ShellMetacharacters = [';', '|', '&', '$', '>', '<', '`', '\n'];

    public PathResolver Paths { get; } = new();
    public GitignoreRegistry Gitignore { get; } = new();
    public int DenialCount { get; private set; }

    public IReadOnlyList<string> ForbiddenRoots { get; } =
        [.. forbiddenRoots.Select(root => PathResolver.Normalise(root, "/"))];

    public GuardDecision Inspect(string rawCommand)
    {
        var decision = Judge(rawCommand);
        if (!decision.IsAllowed)
            DenialCount++;

        return decision;
    }

    private GuardDecision Judge(string rawCommand)
    {
        var parsed = CommandParser.Parse(rawCommand);
        if (parsed.Command is null)
            return GuardDecision.Deny($"Rejected before sending: {parsed.Error}");

        var command = parsed.Command;

        if (command.Verb == "reboot")
            return GuardDecision.Deny("Rebooting rebuilds the machine and throws away every change made so far, so it is not available here. Use the dedicated reboot tool if the machine really needs to be reset.");

        foreach (var argument in command.Arguments)
        {
            var verdict = argument.Kind switch
            {
                ArgumentKind.Path => JudgePath(argument.Value),
                ArgumentKind.NamePattern => JudgeNamePattern(argument.Value),
                _ => null
            };

            if (verdict is not null)
                return GuardDecision.Deny(verdict);
        }

        return GuardDecision.Allow(command);
    }

    private string? JudgePath(string raw)
    {
        var metacharacter = raw.IndexOfAny(ShellMetacharacters);
        if (metacharacter >= 0)
            return $"Rejected before sending: the path '{raw}' contains '{raw[metacharacter]}'. This machine runs a fixed command dispatcher, not a shell, so a path is only ever a plain path.";

        if (!Paths.TryResolve(raw, out var absolute, out var error))
            return $"Rejected before sending: {error}";

        var forbiddenRoot = ForbiddenRoots.FirstOrDefault(root => PathResolver.IsWithin(absolute, root));
        if (forbiddenRoot is not null)
            return $"Rejected before sending: '{raw}' resolves to '{absolute}', which is inside the off-limits directory '{forbiddenRoot}'. Touching it would cut off access and reset the machine.";

        if (Gitignore.BlockingUnreadDirectory(absolute) is { } unreadDirectory)
            return $"Rejected before sending: '{unreadDirectory}' has a .gitignore that has not been read yet, so it is unknown whether '{absolute}' is on its blacklist. Run 'cat {unreadDirectory.TrimEnd('/')}/.gitignore' first.";

        var judgement = Gitignore.Judge(absolute);
        if (judgement.IsIgnored)
            return $"Rejected before sending: '{absolute}' is blacklisted by the rule '{judgement.Rule}' in {judgement.Directory}/.gitignore. Files listed there must not be touched at all.";

        return null;
    }

    /// <summary>
    /// 'find' takes a name pattern, not a path, and searches the whole filesystem. A pattern that
    /// names an off-limits directory is the one way this command can reach into one.
    /// </summary>
    private string? JudgeNamePattern(string pattern)
    {
        if (pattern.Contains('/'))
            return $"Rejected before sending: 'find' matches file names, not paths, so '{pattern}' will not work. Search for the name alone.";

        // Wildcards are stripped before comparing, so 'etc' and '*etc*' are both caught while an
        // ordinary name that merely contains one of the words ('protocol.txt') is left alone.
        var searchedName = pattern.Replace("*", string.Empty).Replace("?", string.Empty);
        var forbiddenName = ForbiddenRoots
            .Select(root => root.Trim('/'))
            .FirstOrDefault(name => name.Length > 0 && string.Equals(name, searchedName, StringComparison.OrdinalIgnoreCase));

        return forbiddenName is not null
            ? $"Rejected before sending: the pattern '{pattern}' names the off-limits directory '{forbiddenName}'."
            : null;
    }

    public void ObserveWorkingDirectory(string absoluteDirectory) => Paths.Synchronise(absoluteDirectory);

    /// <summary>
    /// A 'cd' whose outcome is unclear leaves the mirrored working directory untrustworthy, and an
    /// untrustworthy mirror makes every later relative path unjudgeable — so it is dropped instead.
    /// </summary>
    public void ObserveDirectoryChange(ShellCommand command, bool succeeded)
    {
        if (!succeeded)
        {
            Paths.Desynchronise();
            return;
        }

        var target = command.Arguments.FirstOrDefault()?.Value ?? "/";
        if (Paths.TryResolve(target, out var absolute, out _))
            Paths.Synchronise(absolute);
        else
            Paths.Desynchronise();
    }

    public void ObserveListing(string directory, IEnumerable<string> entryNames)
    {
        if (entryNames.Any(name => name.Trim() == ".gitignore"))
            Gitignore.NoteDiscovered(directory);
    }

    public void ObserveGitignore(string absolutePath, string content) =>
        Gitignore.Register(PathResolver.DirectoryOf(absolutePath), content);

    public string Describe()
    {
        var lines = new List<string>
        {
            $"working directory: {(Paths.IsSynchronised ? Paths.CurrentDirectory : "unknown (run pwd)")}",
            $"off-limits: {string.Join(", ", ForbiddenRoots)}"
        };

        if (Gitignore.KnownDirectories.Count > 0)
            lines.Add($".gitignore loaded from: {string.Join(", ", Gitignore.KnownDirectories)}");

        if (Gitignore.UnreadDirectories.Count > 0)
            lines.Add($".gitignore seen but not read: {string.Join(", ", Gitignore.UnreadDirectories)}");

        return string.Join(Environment.NewLine, lines);
    }
}
