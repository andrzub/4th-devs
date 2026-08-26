using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ProxyServer.Llm;

namespace ProxyServer.Mcp;

/// <summary>
/// Connects to the configured MCP servers and exposes their tools in the shape the LLM layer
/// needs: a flat list of Function Calling schemas plus a name-based invoker.
/// From the agent's point of view an MCP tool is indistinguishable from a native one.
/// </summary>
public sealed class McpToolGateway : IAsyncDisposable
{
    private readonly List<McpClient> _clients = [];
    private readonly Dictionary<string, McpClientTool> _toolsByExposedName = [];
    private readonly List<ToolDefinition> _definitions = [];
    private readonly ILogger<McpToolGateway> _logger;

    private McpToolGateway(ILogger<McpToolGateway> logger) => _logger = logger;

    public IReadOnlyList<ToolDefinition> Tools => _definitions;

    public static async Task<McpToolGateway> ConnectAsync(
        McpServersConfig config,
        string rootDirectory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken = default)
    {
        var gateway = new McpToolGateway(loggerFactory.CreateLogger<McpToolGateway>());

        foreach (var (serverName, entry) in config.McpServers)
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = serverName,
                Command = entry.Command,
                Arguments = entry.Args,
                EnvironmentVariables = entry.Env,
                WorkingDirectory = ResolveDirectory(entry.WorkingDirectory, rootDirectory)
            }, loggerFactory);

            var client = await McpClient.CreateAsync(transport, loggerFactory: loggerFactory, cancellationToken: cancellationToken);
            gateway._clients.Add(client);

            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            foreach (var tool in tools)
                gateway.Register(serverName, tool);

            gateway._logger.LogInformation("MCP server '{Server}' connected, {Count} tool(s): {Tools}",
                serverName, tools.Count, string.Join(", ", tools.Select(t => t.Name)));
        }

        return gateway;
    }

    /// <summary>
    /// Registers a tool under its plain name, falling back to a "server__tool" prefix when
    /// another server already claimed that name — the host is responsible for avoiding collisions.
    /// </summary>
    private void Register(string serverName, McpClientTool tool)
    {
        var exposedName = _toolsByExposedName.ContainsKey(tool.Name) ? $"{serverName}__{tool.Name}" : tool.Name;

        _toolsByExposedName[exposedName] = tool;
        _definitions.Add(new ToolDefinition(exposedName, tool.Description ?? string.Empty, tool.JsonSchema));
    }

    public async Task<string> InvokeAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        if (!_toolsByExposedName.TryGetValue(toolName, out var tool))
            return $"Nieznane narzędzie '{toolName}'. Dostępne narzędzia: {string.Join(", ", _toolsByExposedName.Keys)}.";

        IReadOnlyDictionary<string, object?> arguments;
        try
        {
            arguments = ParseArguments(argumentsJson);
        }
        catch (JsonException ex)
        {
            return $"Argumenty nie są poprawnym JSON-em ({ex.Message}). Wywołaj narzędzie ponownie z poprawnym obiektem argumentów.";
        }

        try
        {
            var result = await tool.CallAsync(arguments, cancellationToken: cancellationToken);
            return Flatten(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP tool '{Tool}' failed", toolName);
            return $"Wywołanie narzędzia '{toolName}' nie udało się: {ex.Message}. Możesz spróbować ponownie lub poinformować operatora o chwilowym problemie z systemem.";
        }
    }

    private static Dictionary<string, object?> ParseArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return [];

        var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson) ?? [];
        return parsed.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
    }

    private static string Flatten(CallToolResult result)
    {
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

        if (string.IsNullOrWhiteSpace(text))
            text = result.StructuredContent?.GetRawText() ?? "(narzędzie nie zwróciło treści)";

        return result.IsError == true ? $"Błąd narzędzia: {text}" : text;
    }

    private static string? ResolveDirectory(string? path, string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return rootDirectory;

        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(rootDirectory, path));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
            await client.DisposeAsync();
    }
}
