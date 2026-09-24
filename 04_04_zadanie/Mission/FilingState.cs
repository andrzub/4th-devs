using _04_04_zadanie.Filesystem;
using _04_04_zadanie.Notes;

namespace _04_04_zadanie.Mission;

/// <summary>
/// Everything the run knows about its own progress, kept by code rather than taken from the model:
/// the projection being built, which notes and templates have actually been read, and whether the
/// plan has passed the validator. The loop ends on the last of these, never on the agent saying so.
/// </summary>
public sealed class FilingState
{
    public FilingState(NoteLibrary notes, Workspace workspace)
    {
        AllNotes = notes.Names;
        AllTemplates = workspace.TemplateNames;
        Sources = ValidationSources.FromNotes(notes);

        foreach (var directory in FilesystemLayout.Directories)
            Filesystem.CreateDirectory(directory);
    }

    public VirtualFilesystem Filesystem { get; } = new();

    public ValidationSources Sources { get; }

    public IReadOnlyList<string> AllNotes { get; }

    public IReadOnlyList<string> AllTemplates { get; }

    public HashSet<string> NotesRead { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> TemplatesRead { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int Writes { get; set; }

    public int Replacements { get; set; }

    public int Refusals { get; set; }

    public int Deletes { get; set; }

    public ValidationReport? LastReport { get; private set; }

    /// <summary>Set only by a validation that found no errors; the goal of the run.</summary>
    public bool Accepted { get; private set; }

    public IReadOnlyList<string> UnreadNotes => AllNotes.Where(note => !NotesRead.Contains(note)).ToList();

    public bool AllNotesRead => UnreadNotes.Count == 0;

    public ValidationReport Check()
    {
        LastReport = PlanValidator.Validate(Filesystem, Sources);
        if (LastReport.IsValid)
            Accepted = true;
        return LastReport;
    }

    public string RenderProgress()
    {
        var counts = string.Join(" | ", FilesystemLayout.Directories.Select(directory => $"{VirtualFilesystem.Name(directory)}: {Filesystem.FilesIn(directory).Count}"));
        var plan = LastReport is null ? "not checked yet" : LastReport.IsValid ? "valid" : $"invalid ({LastReport.Errors} errors)";
        return $"[progress] {counts} | notes read: {NotesRead.Count}/{AllNotes.Count} | templates read: {TemplatesRead.Count}/{AllTemplates.Count} | plan: {plan}";
    }
}
