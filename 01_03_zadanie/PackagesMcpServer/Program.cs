using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PackagesMcpServer;

// ---------------------------------------------------------------------------
// STDIO MCP server exposing the hub packages API as two tools.
// Launched as a child process by the proxy host (see ProxyServer/mcp.json).
// ---------------------------------------------------------------------------

var builder = Host.CreateApplicationBuilder(args);

// STDIO transport owns stdout for JSON-RPC traffic — every log line must go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var apiKey = Environment.GetEnvironmentVariable("AI_DEVS_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    await Console.Error.WriteLineAsync("AI_DEVS_API_KEY is not set — the packages API will reject every call.");
    apiKey = string.Empty;
}

builder.Services.AddSingleton(new PackagesApiClient(apiKey));

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "packages-mcp", Version = "1.0.0" };
    })
    .WithStdioServerTransport()
    .WithTools<PackageTools>();

await builder.Build().RunAsync();
