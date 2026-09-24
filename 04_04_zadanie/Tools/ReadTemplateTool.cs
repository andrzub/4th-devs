using System.Text.Json;
using _04_04_zadanie.Mission;
using _04_04_zadanie.Notes;

namespace _04_04_zadanie.Tools;

/// <summary>Reads one note template of the knowledge base; a directory stays closed for writing until its template was read.</summary>
public sealed class ReadTemplateTool(Workspace workspace, FilingState state) : ITool
{
    public string Name => "read_template";

    public string Description =>
        $"Reads the template that says how a note of one kind is named and written. Templates: {string.Join(", ", workspace.TemplateNames)}. " +
        "The template of a directory has to be read before writing into that directory.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "The template's file name, exactly as listed." }
          },
          "required": ["name"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var name = ToolArguments.Parse(argumentsJson).GetString("name") ?? string.Empty;

        WorkspaceDocument template;
        try
        {
            template = workspace.Template(name);
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
        {
            return Task.FromResult(ex.Message);
        }

        state.TemplatesRead.Add(template.Name);
        return Task.FromResult($"=== templates/{template.Name} ==={Environment.NewLine}{template.Content}");
    }
}
