using System.Text.Json.Nodes;

namespace _02_01_zadanie.Tools;

internal static class ToolArguments
{
    public const string TemplateError = "The 'template' argument is required and must be a non-empty string.";

    public static string? ReadTemplate(string argumentsJson)
    {
        try
        {
            var template = JsonNode.Parse(argumentsJson)?.AsObject()["template"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(template) ? null : template;
        }
        catch
        {
            return null;
        }
    }
}
