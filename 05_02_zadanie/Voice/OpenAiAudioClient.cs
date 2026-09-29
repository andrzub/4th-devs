using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _05_02_zadanie.Voice;

/// <summary>
/// The two OpenAI audio endpoints over plain HttpClient: speech synthesis (text in, MP3 out) and
/// transcription (audio in, text out). Throttling replies are retried, everything else surfaces as is.
/// </summary>
public sealed class OpenAiAudioClient
{
    private const int MaxAttempts = 4;

    private readonly HttpClient _http;
    private readonly VoiceSettings _settings;

    public OpenAiAudioClient(VoiceSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Missing Voice:ApiKey — set it in appsettings.Development.json.");

        _settings = settings;
        _http = new HttpClient { BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(2) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
    }

    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["model"] = _settings.SpeechModel,
            ["voice"] = _settings.Voice,
            ["input"] = text,
            ["response_format"] = "mp3"
        };
        if (!string.IsNullOrWhiteSpace(_settings.SpeechInstructions))
            body["instructions"] = _settings.SpeechInstructions;

        var json = body.ToJsonString();
        using var response = await PostAsync("audio/speech", () => new StringContent(json, Encoding.UTF8, "application/json"), cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <param name="vocabularyHint">Spelling of names and codes the listener should expect. Leave empty to hear the audio the way a stranger would.</param>
    public async Task<string> TranscribeAsync(byte[] audio, string fileName, string model, string? vocabularyHint = null, CancellationToken cancellationToken = default)
    {
        HttpContent BuildForm()
        {
            var file = new ByteArrayContent(audio);
            file.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeFor(fileName));
            var form = new MultipartFormDataContent
            {
                { file, "file", fileName },
                { new StringContent(model), "model" },
                { new StringContent(_settings.Language), "language" },
                { new StringContent("json"), "response_format" }
            };
            if (!string.IsNullOrWhiteSpace(vocabularyHint))
                form.Add(new StringContent(vocabularyHint), "prompt");
            return form;
        }

        using var response = await PostAsync("audio/transcriptions", BuildForm, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("text", out var text) ? text.GetString() ?? string.Empty : string.Empty;
    }

    private async Task<HttpResponseMessage> PostAsync(string path, Func<HttpContent> buildContent, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var content = buildContent();
            var response = await _http.PostAsync(path, content, cancellationToken);
            if (response.IsSuccessStatusCode)
                return response;

            var status = response.StatusCode;
            var retryAfter = response.Headers.RetryAfter?.Delta;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();

            if (status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable && attempt < MaxAttempts)
            {
                var delay = retryAfter ?? TimeSpan.FromSeconds(5 * attempt);
                Console.WriteLine($"  [voice] {(int)status} on {path}, retrying in {delay.TotalSeconds:F0}s (attempt {attempt}/{MaxAttempts})...");
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            throw new HttpRequestException($"OpenAI {path} error {(int)status}: {body}", null, status);
        }
    }

    private static string MediaTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        ".webm" => "audio/webm",
        ".m4a" => "audio/mp4",
        ".flac" => "audio/flac",
        _ => "audio/mpeg"
    };
}
