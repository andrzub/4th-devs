using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace _02_01_zadanie.Categorize;

public sealed record HubResponse(int StatusCode, string Body);

/// <summary>
/// Transport for the categorize task: catalog downloads and prompt submissions to /verify.
/// Transient failures (network errors, 429/5xx) are retried here so the agent never spends an
/// iteration on a transport problem, and submissions are serialised and paced so parallel tool
/// calls cannot flood the hub. Every /verify exchange is appended to a JSONL log with the API
/// key redacted.
/// </summary>
public sealed class HubClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CategorizeOptions _options;
    private readonly string _apiKey;

    private DateTimeOffset _lastCallAt = DateTimeOffset.MinValue;

    public HubClient(string apiKey, CategorizeOptions options)
    {
        _apiKey = apiKey;
        _options = options;
    }

    /// <summary>Number of HTTP requests actually sent to /verify, retries included.</summary>
    public int VerifyRequestsSent { get; private set; }

    public string Redact(string text) =>
        _apiKey.Length > 0 ? text.Replace(_apiKey, "***") : text;

    public async Task<string> FetchCatalogCsvAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync(_options.CsvUrl, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"catalog download returned HTTP {(int)response.StatusCode}: {Redact(body)}");

                return body;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       && attempt < _options.MaxAttemptsPerCall
                                       && !cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"  [hub] catalog download failed (attempt {attempt}): {Redact(ex.Message)} — retrying");
                await Task.Delay(Backoff(attempt), cancellationToken);
            }
        }
    }

    public async Task<HubResponse> SendPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await SendWithRetriesAsync(prompt, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HubResponse> SendWithRetriesAsync(string prompt, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["apikey"] = _apiKey,
            ["task"]   = _options.TaskName,
            ["answer"] = new JsonObject { ["prompt"] = prompt }
        }.ToJsonString();

        for (var attempt = 1; ; attempt++)
        {
            await RespectMinIntervalAsync(cancellationToken);

            HttpStatusCode status;
            string body;
            TimeSpan? retryAfter;

            try
            {
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync(_options.VerifyUrl, content, cancellationToken);

                VerifyRequestsSent++;
                _lastCallAt = DateTimeOffset.UtcNow;

                status     = response.StatusCode;
                body       = await response.Content.ReadAsStringAsync(cancellationToken);
                retryAfter = response.Headers.RetryAfter?.Delta;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       && attempt < _options.MaxAttemptsPerCall
                                       && !cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"  [hub] transport error (attempt {attempt}): {Redact(ex.Message)} — retrying");
                await LogAsync(prompt, attempt, null, ex.Message, cancellationToken);
                await Task.Delay(Backoff(attempt), cancellationToken);
                continue;
            }

            await LogAsync(prompt, attempt, (int)status, body, cancellationToken);

            var isRetryable = status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                                     or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout;

            if (isRetryable && attempt < _options.MaxAttemptsPerCall)
            {
                var wait = retryAfter ?? Backoff(attempt);
                Console.WriteLine($"  [hub] HTTP {(int)status} — waiting {wait.TotalSeconds:0.#}s before retrying");
                await Task.Delay(wait, cancellationToken);
                continue;
            }

            return new HubResponse((int)status, Redact(body));
        }
    }

    private async Task RespectMinIntervalAsync(CancellationToken cancellationToken)
    {
        var elapsed = DateTimeOffset.UtcNow - _lastCallAt;
        if (elapsed >= _options.MinInterval)
            return;

        await Task.Delay(_options.MinInterval - elapsed, cancellationToken);
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt - 1))) + TimeSpan.FromMilliseconds(Random.Shared.Next(250, 1250));

    private async Task LogAsync(string prompt, int attempt, int? status, string body, CancellationToken cancellationToken)
    {
        if (_options.LogPath is not { } path)
            return;

        var entry = new JsonObject
        {
            ["ts"]      = DateTimeOffset.UtcNow.ToString("O"),
            ["attempt"] = attempt,
            ["prompt"]  = prompt,
            ["status"]  = status,
            ["body"]    = Redact(body)
        };

        try
        {
            await File.AppendAllTextAsync(path, entry.ToJsonString() + Environment.NewLine, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [hub] could not write the log: {ex.Message}");
        }
    }
}
