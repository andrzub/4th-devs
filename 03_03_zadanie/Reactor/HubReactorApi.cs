using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _03_03_zadanie.Reactor;

/// <summary>
/// The real reactor. Commands go to the hub's /verify endpoint, while the board is read from the
/// endpoint behind the graphical preview — that read is free and does not advance the blocks, so
/// looking at the reactor never costs the robot a tick.
/// </summary>
public sealed class HubReactorApi(string hubBaseUrl, string apiKey, string logPath, int maxCommands) : IReactorApi
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _baseUrl = hubBaseUrl.TrimEnd('/');
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Name => "hub";

    public int CommandsSent { get; private set; }

    public int RemainingCommands => maxCommands - CommandsSent;

    public async Task<ReactorReply> SendAsync(ReactorCommand command, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (CommandsSent >= maxCommands)
                throw new ReactorBudgetExceededException($"Command budget spent: {maxCommands} commands already sent. The run stops here.");

            var payload = new JsonObject
            {
                ["apikey"] = apiKey,
                ["task"] = "reactor",
                ["answer"] = new JsonObject { ["command"] = command.Name() }
            };

            using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync($"{_baseUrl}/verify", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            CommandsSent++;

            Log("verify", command.Name(), (int)response.StatusCode, body);
            return new ReactorReply((int)response.StatusCode, body, BoardStateParser.TryParse(body));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReactorReply> ReadBoardAsync(CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["key"] = apiKey });
        using var response = await _http.PostAsync($"{_baseUrl}/reactor_backend.php", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Log("preview", null, (int)response.StatusCode, body);
        return new ReactorReply((int)response.StatusCode, body, BoardStateParser.TryParse(body));
    }

    private void Log(string kind, string? command, int statusCode, string body)
    {
        var entry = new JsonObject
        {
            ["timestamp"] = DateTimeOffset.Now.ToString("O"),
            ["kind"] = kind,
            ["command"] = command,
            ["number"] = CommandsSent,
            ["status"] = statusCode,
            ["body"] = body.Replace(apiKey, "***")
        };

        File.AppendAllText(logPath, entry.ToJsonString(new JsonSerializerOptions()) + Environment.NewLine);
    }
}
