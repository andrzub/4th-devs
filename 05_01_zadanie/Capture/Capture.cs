using System.Text.Json.Nodes;

namespace _05_01_zadanie.Capture;

public enum CaptureKind
{
    Transcription,
    Attachment,
    Empty
}

/// <summary>One listen reply, read back from the capture store. The raw body stays the source of truth.</summary>
public sealed record Capture(int Number, int? Code, string? Message, CaptureKind Kind, string? Transcription, string? Meta, long? DeclaredSize, byte[]? Content, string RawPath)
{
    public static Capture Parse(int number, string body, string rawPath)
    {
        var reply = JsonNode.Parse(body) as JsonObject ?? new JsonObject();
        var code = reply["code"] is JsonValue codeValue && codeValue.TryGetValue<int>(out var parsedCode) ? parsedCode : (int?)null;
        var message = reply["message"]?.ToString();
        var transcription = reply["transcription"]?.ToString();
        var attachment = reply["attachment"]?.ToString();
        var meta = reply["meta"]?.ToString();
        var declaredSize = reply["filesize"] is JsonValue sizeValue && sizeValue.TryGetValue<long>(out var size) ? size : (long?)null;

        if (attachment is not null)
            return new Capture(number, code, message, CaptureKind.Attachment, transcription, meta, declaredSize, Convert.FromBase64String(attachment), rawPath);

        return transcription is not null
            ? new Capture(number, code, message, CaptureKind.Transcription, transcription, meta, declaredSize, null, rawPath)
            : new Capture(number, code, message, CaptureKind.Empty, null, meta, declaredSize, null, rawPath);
    }
}
