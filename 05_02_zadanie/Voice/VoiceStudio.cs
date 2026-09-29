using System.Security.Cryptography;
using System.Text;

namespace _05_02_zadanie.Voice;

public sealed record Recording(string Text, int Take, string Path, byte[] Audio, bool FromCache);

/// <summary>
/// Speaks lines and hears recordings back, caching both on disk by content: a take of a line is synthesized
/// once per text and voice setup, and a recording is transcribed once per listener, so rerunning a check costs nothing.
/// </summary>
public sealed class VoiceStudio(OpenAiAudioClient audio, VoiceSettings settings, string directory)
{
    /// <summary>
    /// Where a take of <paramref name="text"/> lives for the current voice setup; changing the voice changes the path.
    /// The synthesizer is not deterministic, so a retake of the same text is a different recording and is stored apart.
    /// </summary>
    public string RecordingPathFor(string text, int take = 1)
    {
        var retake = take > 1 ? $"\ntake {take}" : "";
        return Path.Combine(directory, Hash($"{settings.SpeechModel}\n{settings.Voice}\n{settings.SpeechInstructions}\n{text}{retake}") + ".mp3");
    }

    public async Task<Recording> RecordAsync(string text, int take = 1, CancellationToken cancellationToken = default)
    {
        var path = RecordingPathFor(text, take);
        if (File.Exists(path))
            return new Recording(text, take, path, await File.ReadAllBytesAsync(path, cancellationToken), FromCache: true);

        var bytes = await audio.SynthesizeAsync(text, cancellationToken);
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return new Recording(text, take, path, bytes, FromCache: false);
    }

    public async Task<string> HearAsync(byte[] recording, string fileName, string model, string? vocabularyHint = null, CancellationToken cancellationToken = default)
    {
        var hint = string.IsNullOrWhiteSpace(vocabularyHint) ? "" : "-" + Hash(vocabularyHint)[..8];
        var path = Path.Combine(directory, $"{Hash(recording)}-{model}-{settings.Language}{hint}.txt");
        if (File.Exists(path))
            return await File.ReadAllTextAsync(path, cancellationToken);

        var transcript = await audio.TranscribeAsync(recording, fileName, model, vocabularyHint, cancellationToken);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, transcript, cancellationToken);
        return transcript;
    }

    private static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant();
}
