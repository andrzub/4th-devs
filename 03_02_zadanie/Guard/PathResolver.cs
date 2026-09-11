namespace _03_02_zadanie.Guard;

/// <summary>
/// Mirrors the working directory of the remote machine so that every path can be turned into an
/// absolute, collapsed form before it is judged. Without the mirror a relative argument says
/// nothing: <c>cd /</c> followed by <c>cat etc</c> reaches a forbidden directory while naming
/// nothing forbidden. When the mirror is not trusted, relative paths are refused rather than guessed.
/// </summary>
public sealed class PathResolver
{
    public string CurrentDirectory { get; private set; } = "/";

    /// <summary>False until a <c>pwd</c> has confirmed where the session actually starts.</summary>
    public bool IsSynchronised { get; private set; }

    public void Synchronise(string absoluteDirectory)
    {
        CurrentDirectory = Normalise(absoluteDirectory, "/");
        IsSynchronised = true;
    }

    /// <summary>Dropped after a <c>cd</c> whose outcome could not be read, so the next relative path is refused.</summary>
    public void Desynchronise() => IsSynchronised = false;

    public bool TryResolve(string raw, out string absolute, out string? error)
    {
        absolute = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Empty path.";
            return false;
        }

        if (raw.StartsWith('~'))
        {
            error = "Home-relative paths ('~') cannot be checked against the blacklist. Use an absolute path.";
            return false;
        }

        if (!raw.StartsWith('/') && !IsSynchronised)
        {
            error = "The working directory of this session is not known yet, so a relative path cannot be resolved safely. Run 'pwd' first, or use an absolute path.";
            return false;
        }

        absolute = Normalise(raw, CurrentDirectory);
        return true;
    }

    /// <summary>
    /// Collapses a path lexically, which is the point: '/opt/firmware/../../etc' has to become
    /// '/etc' before anything decides whether it is allowed.
    /// </summary>
    public static string Normalise(string raw, string relativeTo)
    {
        var combined = raw.StartsWith('/') ? raw : $"{relativeTo.TrimEnd('/')}/{raw}";
        var segments = new List<string>();

        foreach (var segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;

            if (segment == "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return "/" + string.Join('/', segments);
    }

    public static string DirectoryOf(string absolutePath)
    {
        var lastSlash = absolutePath.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : absolutePath[..lastSlash];
    }

    public static string NameOf(string absolutePath) => absolutePath[(absolutePath.LastIndexOf('/') + 1)..];

    /// <summary>True when <paramref name="path"/> is the directory itself or lives underneath it.</summary>
    public static bool IsWithin(string path, string directory)
    {
        var root = directory == "/" ? "/" : directory.TrimEnd('/');
        return path == root || path.StartsWith(root == "/" ? "/" : root + "/", StringComparison.Ordinal);
    }

    /// <summary>Every directory from the root down to <paramref name="path"/>, root first.</summary>
    public static IEnumerable<string> AncestorsOf(string path)
    {
        yield return "/";

        var built = string.Empty;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // The last segment is the entry itself, not a directory containing it.
        for (var i = 0; i < segments.Length - 1; i++)
        {
            built += "/" + segments[i];
            yield return built;
        }
    }
}
