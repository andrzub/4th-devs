using System.Text;

namespace _05_01_zadanie.Capture;

/// <summary>
/// Identifies decoded attachments by their leading bytes, independently of the declared MIME type,
/// so a mislabelled file is visible in triage instead of being routed by its label.
/// </summary>
public static class FileSniffer
{
    private static readonly (byte[] Magic, int Offset, string Type)[] Signatures =
    [
        ([0x89, 0x50, 0x4E, 0x47], 0, "image/png"),
        ([0xFF, 0xD8, 0xFF], 0, "image/jpeg"),
        ("GIF8"u8.ToArray(), 0, "image/gif"),
        ("WEBP"u8.ToArray(), 8, "image/webp"),
        ("%PDF"u8.ToArray(), 0, "application/pdf"),
        ("PK\x03\x04"u8.ToArray(), 0, "application/zip"),
        ([0x1F, 0x8B], 0, "application/gzip"),
        ("ID3"u8.ToArray(), 0, "audio/mpeg"),
        ([0xFF, 0xFB], 0, "audio/mpeg"),
        ([0xFF, 0xF3], 0, "audio/mpeg"),
        ("WAVE"u8.ToArray(), 8, "audio/wav"),
        ("OggS"u8.ToArray(), 0, "audio/ogg"),
        ("fLaC"u8.ToArray(), 0, "audio/flac"),
        ("ftyp"u8.ToArray(), 4, "video/mp4"),
    ];

    public static string Sniff(byte[] content)
    {
        foreach (var (magic, offset, type) in Signatures)
            if (content.Length >= offset + magic.Length && content.AsSpan(offset, magic.Length).SequenceEqual(magic))
                return type;

        return LooksLikeText(content) ? SniffText(content) : "application/octet-stream";
    }

    public static string Extension(string mime) => mime.Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "application/json" => "json",
        "text/plain" => "txt",
        "text/csv" => "csv",
        "text/html" => "html",
        "text/markdown" => "md",
        "application/xml" or "text/xml" => "xml",
        "image/png" => "png",
        "image/jpeg" => "jpg",
        "image/gif" => "gif",
        "image/webp" => "webp",
        "application/pdf" => "pdf",
        "application/zip" => "zip",
        "application/gzip" => "gz",
        "audio/mpeg" => "mp3",
        "audio/wav" or "audio/x-wav" => "wav",
        "audio/ogg" => "ogg",
        "audio/flac" => "flac",
        "video/mp4" => "mp4",
        _ => "bin"
    };

    public static bool LooksLikeText(byte[] content)
    {
        var sample = content.AsSpan(0, Math.Min(content.Length, 4096));
        if (sample.IndexOf((byte)0) >= 0)
            return false;

        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(sample.Length == content.Length ? sample : TrimPartialUtf8(sample));
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string SniffText(byte[] content)
    {
        var head = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 512)).TrimStart('﻿', ' ', '\r', '\n', '\t');
        return head switch
        {
            _ when head.StartsWith('{') || head.StartsWith('[') => "application/json",
            _ when head.StartsWith("<?xml") => "application/xml",
            _ when head.StartsWith('<') => "text/html",
            _ => "text/plain"
        };
    }

    /// <summary>Drops a multi-byte UTF-8 sequence cut in half by the sample boundary.</summary>
    private static ReadOnlySpan<byte> TrimPartialUtf8(ReadOnlySpan<byte> sample)
    {
        var end = sample.Length;
        for (var back = 1; back <= 3 && end - back >= 0; back++)
        {
            var b = sample[end - back];
            if ((b & 0xC0) != 0x80)
                return (b & 0x80) == 0 ? sample : sample[..(end - back)];
        }

        return sample;
    }
}
