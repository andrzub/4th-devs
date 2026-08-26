using System.Text.Json;
using System.Text.Json.Serialization;
using ProxyServer.Agent;
using ProxyServer.Llm;
using ProxyServer.Mcp;
using ProxyServer.Mission;
using ProxyServer.Sessions;

// ---------------------------------------------------------------------------
// S01E03 "proxy" — HTTP endpoint acting as a logistics-operator assistant.
// This app is the MCP host: it owns the MCP client that connects to the
// packages MCP server and feeds its tools to the model as Function Calling schemas.
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

// Loaded explicitly so secrets are also picked up when running outside the Development environment.
builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);

var aiDevsApiKey = builder.Configuration["AI_DevsApiKey"] ?? throw new InvalidOperationException("AI_DevsApiKey is not configured.");
var openAiApiKey = builder.Configuration["OpenAI:ApiKey"] ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
var openAiBaseUrl = builder.Configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1";
var openAiModel = builder.Configuration["OpenAI:DefaultModel"] ?? "gpt-4.1";

var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory) ?? builder.Environment.ContentRootPath;
var mcpConfig = LoadMcpConfig(Path.Combine(AppContext.BaseDirectory, "mcp.json"), aiDevsApiKey);

using var startupLoggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole(options => options.SingleLine = true));

await using var gateway = await McpToolGateway.ConnectAsync(mcpConfig, solutionRoot, startupLoggerFactory);

builder.Services.AddSingleton<ILlmClient>(new OpenAiLlmClient(openAiApiKey, openAiBaseUrl, openAiModel));
builder.Services.AddSingleton(gateway);
builder.Services.AddSingleton<ReactorPackageGuard>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<ProxyAgent>();

var app = builder.Build();

app.MapGet("/", () => Results.Text("Centrum Nadzoru Przesyłek Kolejowych — POST {\"sessionID\":\"...\",\"msg\":\"...\"}"));

app.MapGet("/health", (SessionStore sessions, McpToolGateway tools) =>
    Results.Ok(new { status = "ok", sessions = sessions.Count, tools = tools.Tools.Select(t => t.Name) }));

var handleMessage = async (
    ProxyRequest? request,
    ProxyAgent agent,
    SessionStore sessions,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Msg))
        return Results.Ok(new ProxyResponse("Nie widzę treści wiadomości — napisz jeszcze raz."));

    var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? "default" : request.SessionId.Trim();
    var session = sessions.GetOrCreate(sessionId);

    logger.LogInformation("[{Session}] operator: {Message}", sessionId, request.Msg);

    string reply;
    try
    {
        reply = await agent.HandleAsync(session, request.Msg, cancellationToken);
    }
    catch (Exception ex)
    {
        // The operator must never see a stack trace — stay in character and let them retry.
        logger.LogError(ex, "[{Session}] turn failed", sessionId);
        reply = "Coś mi tu system przymula, daj mi chwilę i powtórz proszę ostatnią wiadomość.";
    }

    logger.LogInformation("[{Session}] reply: {Reply}", sessionId, reply);

    return Results.Ok(new ProxyResponse(reply));
};

// The hub is given one URL, so both the root and the explicit path accept messages.
app.MapPost("/", handleMessage);
app.MapPost("/api/proxy", handleMessage);

app.Run();

static McpServersConfig LoadMcpConfig(string path, string aiDevsApiKey)
{
    if (!File.Exists(path))
        throw new FileNotFoundException($"mcp.json not found at '{path}'.", path);

    var config = JsonSerializer.Deserialize<McpServersConfig>(File.ReadAllText(path), new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }) ?? throw new InvalidOperationException($"'{path}' is not a valid MCP configuration.");

    // The api key lives in appsettings.Development.json, never in the committed mcp.json.
    foreach (var entry in config.McpServers.Values)
        entry.Env["AI_DEVS_API_KEY"] = aiDevsApiKey;

    return config;
}

static string? FindSolutionRoot(string startDirectory)
{
    for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
    {
        if (dir.GetFiles("*.slnx").Length > 0)
            return dir.FullName;
    }

    return null;
}

internal sealed record ProxyRequest(
    [property: JsonPropertyName("sessionID")] string? SessionId,
    [property: JsonPropertyName("msg")] string? Msg);

internal sealed record ProxyResponse([property: JsonPropertyName("msg")] string Msg);
