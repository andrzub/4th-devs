namespace _05_02_zadanie.Hub;

/// <summary>Recognises an audio container by its leading bytes, so the operator's recording is transcribed under the right extension whatever the reply calls it.</summary>
public static class AudioSniffer
{
    private static readonly (byte[] Magic, int Offset, string Extension)[] Signatures =
    [
        ("ID3"u8.ToArray(), 0, "mp3"),
        ([0xFF, 0xFB], 0, "mp3"),
        ([0xFF, 0xF3], 0, "mp3"),
        ([0xFF, 0xF2], 0, "mp3"),
        ("WAVE"u8.ToArray(), 8, "wav"),
        ("OggS"u8.ToArray(), 0, "ogg"),
        ("fLaC"u8.ToArray(), 0, "flac"),
        ([0x1A, 0x45, 0xDF, 0xA3], 0, "webm"),
        ("ftyp"u8.ToArray(), 4, "m4a"),
    ];

    /// <summary>The file extension of a recognised audio container, or null when the bytes are not audio.</summary>
    public static string? Extension(byte[] content)
    {
        foreach (var (magic, offset, extension) in Signatures)
            if (content.Length >= offset + magic.Length && content.AsSpan(offset, magic.Length).SequenceEqual(magic))
                return extension;

        return null;
    }
}
