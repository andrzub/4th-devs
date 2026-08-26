using System.Text.Json;

namespace ProxyServer.Llm;

/// <summary>
/// A tool schema as advertised to the model. Carries no execution logic — invoking the tool
/// is the job of whoever provided the definition (here: the MCP gateway).
/// </summary>
public sealed record ToolDefinition(string Name, string Description, JsonElement ParametersSchema);
