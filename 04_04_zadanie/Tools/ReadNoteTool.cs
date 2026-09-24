using System.Text.Json;
using _04_04_zadanie.Mission;
using _04_04_zadanie.Notes;

namespace _04_04_zadanie.Tools;

/// <summary>Reads one of Natan's notes in full and records that it was read; writes stay refused until all are.</summary>
public sealed class ReadNoteTool(NoteLibrary notes, FilingState state) : ITool
{
    public string Name => "read_note";

    public string Description =>
        $"Reads one of Natan's notes in full. Names: {string.Join(", ", notes.Names)}. " +
        "Every note has to be read before anything is written.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "The note's file name, exactly as listed." }
          },
          "required": ["name"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var name = ToolArguments.Parse(argumentsJson).GetString("name") ?? string.Empty;

        Note note;
        try
        {
            note = notes.Read(name);
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
        {
            return Task.FromResult(ex.Message);
        }

        state.NotesRead.Add(note.Name);
        return Task.FromResult($"=== {note.Name} ==={Environment.NewLine}{note.Content}");
    }
}
