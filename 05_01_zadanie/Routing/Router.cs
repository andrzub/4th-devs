using _05_01_zadanie.Capture;

namespace _05_01_zadanie.Routing;

public enum Route
{
    /// <summary>Plain speech, goes to the analyst verbatim.</summary>
    Speech,

    /// <summary>Static; only its distinct fragments reach the analyst, in one shared section.</summary>
    Noise,

    /// <summary>Decoded locally; the analyst sees the decoded text.</summary>
    Morse,

    /// <summary>Text-based file (JSON, CSV, XML, plain text), read and compacted by code.</summary>
    Document,

    /// <summary>Needs a speech-to-text model.</summary>
    Audio,

    /// <summary>Needs a vision model.</summary>
    Image,

    /// <summary>Nothing to analyse, or a file nobody here can read.</summary>
    Skip
}

public sealed record RoutedCapture(Capture.Capture Capture, Route Route, string Reason, string? LocalText = null);

/// <summary>
/// Decides, by code alone, what happens to every capture. The file type is taken from the bytes, not
/// the label, so a mislabelled binary cannot sneak into a text prompt as Base64.
/// </summary>
public static class Router
{
    public static RoutedCapture Decide(Capture.Capture capture, int maxInlineDocumentBytes) => capture.Kind switch
    {
        CaptureKind.Empty => new(capture, Route.Skip, $"no material (code {capture.Code})"),
        CaptureKind.Transcription => DecideTranscription(capture),
        _ => DecideAttachment(capture, maxInlineDocumentBytes)
    };

    private static RoutedCapture DecideTranscription(Capture.Capture capture)
    {
        var text = capture.Transcription!;
        if (MorseDecoder.TryDecode(text, out var decoded))
            return new(capture, Route.Morse, "Ti/Ta syllables decoded as Morse", decoded);

        return NoiseFilter.IsNoise(text)
            ? new(capture, Route.Noise, "static pattern: many cut-off fragments between radio sounds")
            : new(capture, Route.Speech, "speech transcription", text.Trim());
    }

    private static RoutedCapture DecideAttachment(Capture.Capture capture, int maxInlineDocumentBytes)
    {
        var content = capture.Content!;
        var type = FileSniffer.Sniff(content);

        if (type.StartsWith("audio/"))
            return new(capture, Route.Audio, $"{type}, {content.Length:N0} B, needs speech-to-text");

        if (type.StartsWith("image/"))
            return new(capture, Route.Image, $"{type}, {content.Length:N0} B, needs vision");

        if (!FileSniffer.LooksLikeText(content))
            return new(capture, Route.Skip, $"{type}, {content.Length:N0} B, binary format without a reader");

        if (content.Length > maxInlineDocumentBytes)
            return new(capture, Route.Skip, $"{type}, {content.Length:N0} B, text file above the inline limit of {maxInlineDocumentBytes:N0} B");

        return new(capture, Route.Document, $"{type}, {content.Length:N0} B, read locally", DocumentCompactor.Compact(content, type));
    }
}
