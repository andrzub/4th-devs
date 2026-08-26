namespace ProxyServer.Mcp;

/// <summary>
/// Shape of mcp.json — the list of MCP servers this host connects to.
/// </summary>
public sealed class McpServersConfig
{
    public Dictionary<string, McpServerEntry> McpServers { get; set; } = [];
}

public sealed class McpServerEntry
{
    /// <summary>Executable to launch for a STDIO server, e.g. "dotnet".</summary>
    public string Command { get; set; } = string.Empty;

    public List<string> Args { get; set; } = [];

    /// <summary>Environment variables passed to the server process (e.g. API keys).</summary>
    public Dictionary<string, string?> Env { get; set; } = [];

    /// <summary>Working directory for the server process. Relative paths resolve against the solution root.</summary>
    public string? WorkingDirectory { get; set; }
}
