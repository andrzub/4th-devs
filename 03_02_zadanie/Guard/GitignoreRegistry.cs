namespace _03_02_zadanie.Guard;

public sealed record GitignoreVerdict(bool IsIgnored, string? Rule, string? Directory);

/// <summary>
/// The blacklist the machine declares about itself. The task treats a .gitignore as untouchable
/// territory, so the rules cannot live in the prompt: they are discovered at runtime and enforced
/// here. A directory whose .gitignore has been seen but not yet read counts as fully blocked —
/// an unknown blacklist is treated as the worst case, not as an empty one.
/// </summary>
public sealed class GitignoreRegistry
{
    private readonly Dictionary<string, List<GitignorePattern>> _rulesByDirectory = [];
    private readonly HashSet<string> _unreadDirectories = [];

    public IReadOnlyCollection<string> KnownDirectories => _rulesByDirectory.Keys;
    public IReadOnlyCollection<string> UnreadDirectories => _unreadDirectories;

    /// <summary>Records that a listing revealed a .gitignore whose content is still unknown.</summary>
    public void NoteDiscovered(string directory)
    {
        var normalised = PathResolver.Normalise(directory, "/");
        if (!_rulesByDirectory.ContainsKey(normalised))
            _unreadDirectories.Add(normalised);
    }

    public void Register(string directory, string content)
    {
        var normalised = PathResolver.Normalise(directory, "/");
        var patterns = content
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(GitignorePattern.TryParse)
            .OfType<GitignorePattern>()
            .ToList();

        _rulesByDirectory[normalised] = patterns;
        _unreadDirectories.Remove(normalised);
    }

    /// <summary>
    /// The directory of an unread .gitignore that covers this path, if there is one. Reading that
    /// file is always allowed, otherwise the rule would deadlock against itself.
    /// </summary>
    public string? BlockingUnreadDirectory(string absolutePath)
    {
        if (PathResolver.NameOf(absolutePath) == ".gitignore")
            return null;

        return _unreadDirectories.FirstOrDefault(directory => PathResolver.IsWithin(absolutePath, directory));
    }

    public GitignoreVerdict Judge(string absolutePath)
    {
        var verdict = new GitignoreVerdict(false, null, null);

        // Rules from an outer directory apply to everything below it, and within one file the last
        // matching rule wins, so both loops run to the end rather than stopping at the first hit.
        foreach (var directory in _rulesByDirectory.Keys.OrderBy(d => d.Length))
        {
            if (!PathResolver.IsWithin(absolutePath, directory))
                continue;

            var relative = RelativeTo(absolutePath, directory);
            if (relative.Length == 0)
                continue;

            foreach (var pattern in _rulesByDirectory[directory].Where(p => p.Matches(relative)))
                verdict = new GitignoreVerdict(!pattern.IsNegated, pattern.Source, directory);
        }

        return verdict;
    }

    private static string RelativeTo(string absolutePath, string directory)
    {
        var root = directory == "/" ? "/" : directory + "/";
        return absolutePath.StartsWith(root, StringComparison.Ordinal) ? absolutePath[root.Length..] : string.Empty;
    }
}
