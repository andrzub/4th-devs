using System.Security.Cryptography;
using System.Text.Json;
using _05_01_zadanie.Llm;
using _05_01_zadanie.Routing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace _05_01_zadanie.Media;

public sealed record MediaReading(int CaptureNumber, Route Route, string Model, string Text, int PromptTokens, int CompletionTokens, bool FromCache);

/// <summary>
/// The only place binary material meets a model: one call per file, the bytes attached as a proper
/// audio or image part (never as Base64 inside text), images downscaled first. Readings are cached on
/// disk by content hash, model and prompt, so a rerun of the pipeline does not pay for them again.
/// </summary>
public sealed class MediaInterpreter(ILlmClient client, string model, string cacheDirectory, int imageMaxEdgePixels)
{
    private const string AudioPrompt = """
        Transcribe this radio recording verbatim, in its original language (most likely Polish).
        Keep every name, number, phone number and place exactly as spoken; write numbers as digits.
        Mark unintelligible passages as [niesłyszalne]. Do not summarise, translate or comment.
        """;

    private const string ImagePrompt = """
        This image was intercepted from a radio channel. Report what it contains, in Polish:
        1. Transcribe every piece of visible text exactly as written (names, numbers, phone numbers, labels, captions).
        2. Describe briefly what the image shows, including any map, table, sign or document and the facts it states.
        If something cannot be read reliably, say so explicitly instead of guessing. No speculation beyond the image.
        """;

    public async Task<MediaReading> ReadAsync(RoutedCapture routed, CancellationToken cancellationToken = default)
    {
        var content = routed.Capture.Content ?? throw new ArgumentException("Only attachments can be read as media.");
        var prompt = routed.Route switch
        {
            Route.Audio => AudioPrompt,
            Route.Image => ImagePrompt,
            _ => throw new ArgumentException($"Route {routed.Route} is not a media route.")
        };

        var cachePath = Path.Combine(cacheDirectory, $"{CacheKey(content, prompt)}.json");
        if (File.Exists(cachePath) && JsonSerializer.Deserialize<MediaReading>(File.ReadAllText(cachePath)) is { } cached)
            return cached with { CaptureNumber = routed.Capture.Number, FromCache = true };

        var message = routed.Route == Route.Audio
            ? Message.UserWithAudio(prompt, [new AudioClip(content, AudioFormat(routed.Capture))])
            : Message.UserWithImages(prompt, [DownscaledDataUrl(content)]);

        var response = await client.CompleteAsync(new LlmRequest { Model = model, Messages = [message], Temperature = 0 }, cancellationToken);
        var reading = new MediaReading(routed.Capture.Number, routed.Route, model, response.Content?.Trim() ?? string.Empty,
            response.Usage.PromptTokens, response.Usage.CompletionTokens, FromCache: false);

        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(cachePath, JsonSerializer.Serialize(reading));
        return reading;
    }

    private string CacheKey(byte[] content, string prompt)
    {
        var material = SHA256.HashData([.. content, .. System.Text.Encoding.UTF8.GetBytes($"{model}\n{prompt}\n{imageMaxEdgePixels}")]);
        return Convert.ToHexStringLower(material)[..24];
    }

    private static string AudioFormat(Capture.Capture capture) =>
        Capture.FileSniffer.Sniff(capture.Content!) == "audio/wav" ? "wav" : "mp3";

    /// <summary>Longest edge capped and re-encoded as JPEG: a 560 KB PNG becomes a fraction of that, legibility intact.</summary>
    private string DownscaledDataUrl(byte[] content)
    {
        using var image = Image.Load(content);
        var scale = Math.Min(1.0, imageMaxEdgePixels / (double)Math.Max(image.Width, image.Height));
        if (scale < 1.0)
            image.Mutate(ctx => ctx.Resize((int)(image.Width * scale), (int)(image.Height * scale)));

        using var output = new MemoryStream();
        image.SaveAsJpeg(output, new JpegEncoder { Quality = 85 });
        return $"data:image/jpeg;base64,{Convert.ToBase64String(output.ToArray())}";
    }
}
