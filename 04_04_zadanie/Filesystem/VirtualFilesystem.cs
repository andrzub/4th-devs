using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _04_04_zadanie.Filesystem;

/// <summary>
/// The filesystem the agent builds, kept locally until the whole structure has been judged and can
/// be sent in one batch. Nothing the agent writes reaches the hub directly: a wrong file costs a tool
/// turn here and a whole verification there.
/// </summary>
public sealed class VirtualFilesystem
{
    private static readonly JsonSerializerOptions PlanJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly SortedSet<string> _directories = new(StringComparer.Ordinal) { "/" };
    private readonly SortedDictionary<string, string> _files = new(StringComparer.Ordinal);

    /// <summary>Every directory except the root, parents before children.</summary>
    public IReadOnlyList<string> Directories => _directories.Where(path => path != "/").ToList();

    public IReadOnlyDictionary<string, string> Files => _files;

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("The path is empty.");

        var trimmed = path.Trim();
        if (!trimmed.StartsWith('/'))
            throw new ArgumentException($"The path must be absolute and start with '/': '{path}'.");
        if (trimmed.Any(char.IsWhiteSpace))
            throw new ArgumentException($"The path contains whitespace: '{path}'.");

        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
            throw new ArgumentException($"The path walks the tree with '.' or '..': '{path}'.");

        return "/" + string.Join('/', segments);
    }

    public static string Parent(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash <= 0 ? "/" : path[..slash];
    }

    public static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];

    public bool DirectoryExists(string path) => _directories.Contains(NormalizePath(path));

    public bool FileExists(string path) => _files.ContainsKey(NormalizePath(path));

    public void CreateDirectory(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized == "/")
            return;
        if (_files.ContainsKey(normalized))
            throw new InvalidOperationException($"'{normalized}' is a file.");
        if (!_directories.Contains(Parent(normalized)))
            throw new InvalidOperationException($"The parent directory '{Parent(normalized)}' does not exist.");

        _directories.Add(normalized);
    }

    /// <returns>True when an existing file was replaced.</returns>
    public bool WriteFile(string path, string content)
    {
        var normalized = NormalizePath(path);
        if (_directories.Contains(normalized))
            throw new InvalidOperationException($"'{normalized}' is a directory.");
        if (!_directories.Contains(Parent(normalized)))
            throw new InvalidOperationException($"The directory '{Parent(normalized)}' does not exist.");

        var replaced = _files.ContainsKey(normalized);
        _files[normalized] = content;
        return replaced;
    }

    /// <returns>How many entries disappeared: one for a file, the whole subtree for a directory.</returns>
    public int Delete(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized == "/")
            throw new InvalidOperationException("The root cannot be deleted.");
        if (_files.Remove(normalized))
            return 1;
        if (!_directories.Contains(normalized))
            throw new FileNotFoundException($"No such file or directory: '{normalized}'.");

        var prefix = normalized + "/";
        var files = _files.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var file in files)
            _files.Remove(file);

        var directories = _directories.Where(directory => directory == normalized || directory.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (var directory in directories)
            _directories.Remove(directory);

        return files.Count + directories.Count;
    }

    public string ReadFile(string path)
    {
        var normalized = NormalizePath(path);
        return _files.TryGetValue(normalized, out var content) ? content : throw new FileNotFoundException($"No such file: '{normalized}'.");
    }

    /// <summary>Direct children of a directory: subdirectories with a trailing slash, then files.</summary>
    public IReadOnlyList<string> List(string path)
    {
        var normalized = NormalizePath(path);
        if (!_directories.Contains(normalized))
            throw new DirectoryNotFoundException($"No such directory: '{normalized}'.");

        var directories = _directories.Where(directory => directory != normalized && Parent(directory) == normalized).Select(directory => Name(directory) + "/");
        var files = _files.Keys.Where(file => Parent(file) == normalized).Select(Name);
        return directories.Concat(files).ToList();
    }

    public IReadOnlyList<string> FilesIn(string directory)
    {
        var normalized = NormalizePath(directory);
        return _files.Keys.Where(file => Parent(file) == normalized).ToList();
    }

    /// <summary>Every directory and file carrying this name, wherever it sits: the hub keeps names unique across the tree.</summary>
    public IReadOnlyList<string> PathsNamed(string name) =>
        _directories.Where(directory => directory != "/" && Name(directory) == name)
            .Concat(_files.Keys.Where(file => Name(file) == name))
            .ToList();

    public string Render()
    {
        var builder = new StringBuilder();
        foreach (var directory in Directories)
        {
            builder.AppendLine($"{directory}/");
            foreach (var file in FilesIn(directory))
                builder.AppendLine($"  {Name(file)}  ({_files[file].Length} B)");
        }

        foreach (var file in FilesIn("/"))
            builder.AppendLine($"{file}  ({_files[file].Length} B)");

        return builder.ToString().TrimEnd();
    }

    public string ToJson()
    {
        var files = new JsonObject();
        foreach (var (path, content) in _files)
            files[path] = content;

        return new JsonObject
        {
            ["directories"] = new JsonArray(Directories.Select(directory => (JsonNode)directory).ToArray()),
            ["files"] = files
        }.ToJsonString(PlanJson);
    }

    public static VirtualFilesystem FromJson(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("The plan is not a JSON object.");
        var filesystem = new VirtualFilesystem();

        foreach (var directory in root["directories"]?.AsArray() ?? [])
            filesystem.CreateDirectory(directory!.GetValue<string>());

        foreach (var (path, content) in root["files"]?.AsObject() ?? [])
            filesystem.WriteFile(path, content?.GetValue<string>() ?? string.Empty);

        return filesystem;
    }
}
