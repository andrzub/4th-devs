using System.Text;

namespace _04_04_zadanie.Notes;

public sealed record WorkspaceDocument(string Name, string Content);

/// <summary>
/// The human-owned part of the knowledge base: the map of content and the note templates. The
/// agent reads them before filing anything, so the structure of a note is decided here, not by
/// the model on each write.
/// </summary>
public sealed class Workspace
{
    private readonly string _directory;

    public Workspace(string directory)
    {
        _directory = directory;
        if (!File.Exists(Path.Combine(directory, "index.md")))
            throw new FileNotFoundException($"Workspace index not found: {Path.Combine(directory, "index.md")}");
    }

    public WorkspaceDocument Index => new("index.md", File.ReadAllText(Path.Combine(_directory, "index.md"), Encoding.UTF8));

    public IReadOnlyList<string> TemplateNames => Directory.GetFiles(Path.Combine(_directory, "templates"), "*.md")
        .Select(path => Path.GetFileName(path))
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public WorkspaceDocument Template(string name)
    {
        var safeName = Path.GetFileName(name);
        if (safeName.Length == 0 || safeName != name)
            throw new ArgumentException($"Not a template name: '{name}'. Use one of: {string.Join(", ", TemplateNames)}.");

        var path = Path.Combine(_directory, "templates", safeName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No such template: '{name}'. Available: {string.Join(", ", TemplateNames)}.");

        return new WorkspaceDocument(safeName, File.ReadAllText(path, Encoding.UTF8));
    }

    public IReadOnlyList<WorkspaceDocument> ReadAll() => new[] { Index }.Concat(TemplateNames.Select(Template)).ToList();
}
