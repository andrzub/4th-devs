using _04_04_zadanie.Filesystem;
using _04_04_zadanie.Llm;
using _04_04_zadanie.Mission;
using _04_04_zadanie.Tools;

namespace _04_04_zadanie.Agents;

/// <summary>
/// The run's grip on the loop. "Look around before you write" is enforced here rather than hoped
/// for: a write is refused until every note has been read and until the template of its directory
/// has been read. After each call the agent sees the progress counted by code, and an attempt to
/// finish is turned back with the validator's findings until the plan passes.
/// </summary>
public sealed class FilingHooks(FilingState state) : IAgentHooks
{
    public Task<string?> BeforeToolCallAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        if (call.FunctionName != WriteFileTool.ToolName)
            return Task.FromResult<string?>(null);

        if (!state.AllNotesRead)
            return Task.FromResult<string?>($"Refused: read every note before writing. Still unread: {string.Join(", ", state.UnreadNotes)}. The pieces of one person or city are spread across the notes.");

        var path = ToolArguments.Parse(call.ArgumentsJson).GetString("path");
        string directory;
        try
        {
            directory = VirtualFilesystem.Parent(VirtualFilesystem.NormalizePath(path));
        }
        catch (ArgumentException)
        {
            // The guard inside the tool explains a malformed path better than this hook could.
            return Task.FromResult<string?>(null);
        }

        if (!FilesystemLayout.Directories.Contains(directory))
            return Task.FromResult<string?>(null);

        var template = FilesystemLayout.TemplateFor(directory);
        if (!state.TemplatesRead.Contains(template))
            return Task.FromResult<string?>($"Refused: read the template for {directory} first (read_template \"{template}\"), then write.");

        return Task.FromResult<string?>(null);
    }

    public Task<string> AfterToolResultAsync(ToolCall call, string result, CancellationToken cancellationToken = default) =>
        Task.FromResult($"{result}{Environment.NewLine}{state.RenderProgress()}");

    public Task<string?> BeforeFinishAsync(CancellationToken cancellationToken = default)
    {
        var report = state.Check();
        if (report.IsValid)
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(
            $"The plan is not complete yet. The validator found:{Environment.NewLine}{report.Render()}{Environment.NewLine}" +
            "Fix these with write_file or delete_file, then run check_plan.");
    }
}
