using System.Text;

namespace _04_04_zadanie.Notes;

public sealed record Note(string Name, string Content);

/// <summary>
/// Natan's notes as the agent sees them: every file of the notes directory, read in full. The
/// directory is listed rather than the file names being fixed in code, so a note added to the
/// archive reaches the agent without a code change.
/// </summary>
public sealed class NoteLibrary
{
    private readonly string _directory;

    public NoteLibrary(string directory)
    {
        _directory = directory;
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Notes directory not found: {directory}");
    }

    public IReadOnlyList<string> Names => Directory.GetFiles(_directory)
        .Select(path => Path.GetFileName(path))
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<Note> ReadAll() => Names.Select(Read).ToList();

    public Note Read(string name)
    {
        // The name comes from the model: only a plain file name of the notes directory is honoured.
        var safeName = Path.GetFileName(name);
        if (safeName.Length == 0 || safeName != name || name.Contains(".."))
            throw new ArgumentException($"Not a note name: '{name}'. Use one of: {string.Join(", ", Names)}.");

        // One note is named with a Polish letter; a model that asks for it in plain ASCII still gets it.
        var resolved = Names.FirstOrDefault(candidate => candidate == safeName)
                       ?? Names.FirstOrDefault(candidate => Filesystem.PolishText.Fold(candidate).Equals(Filesystem.PolishText.Fold(safeName), StringComparison.OrdinalIgnoreCase))
                       ?? throw new FileNotFoundException($"No such note: '{name}'. Available: {string.Join(", ", Names)}.");

        return new Note(resolved, File.ReadAllText(Path.Combine(_directory, resolved), Encoding.UTF8));
    }
}
