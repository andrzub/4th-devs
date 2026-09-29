using System.Text.Encodings.Web;
using System.Text.Json;
using _05_02_zadanie.Call;

namespace _05_02_zadanie.Voice;

public sealed record LineCheck(string Model, string Transcript, IReadOnlyList<string> MissingTerms);

public sealed record BankEntry(string Key, string Text, int Take, string RecordingPath, int Bytes, IReadOnlyList<LineCheck> Checks)
{
    public bool Passed => Checks.Count > 0 && Checks.All(check => check.MissingTerms.Count == 0);
}

public sealed record BankManifest(DateTimeOffset PreparedAt, string SpeechModel, string Voice, string SpeechInstructions, IReadOnlyList<BankEntry> Entries);

/// <summary>
/// The recordings a call will send, prepared and verified before the call starts. Every script line is
/// synthesized and then transcribed by each independent listener without any hint, and it passes only if
/// every listener heard all of its required terms. A line that fails is synthesized again, up to
/// <see cref="VoiceSettings.MaxTakes"/> times, because a mangled road code in one take is usually fine in the next.
/// The manifest ties each recording to the text and voice setup it was made from, so a call can refuse
/// a bank that is stale or has failed lines.
/// </summary>
public sealed class VoiceBank(VoiceStudio studio, VoiceSettings settings, string manifestPath)
{
    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public string ManifestPath => manifestPath;

    public async Task<IReadOnlyList<BankEntry>> PrepareAsync(IEnumerable<ScriptLine> lines, Action<BankEntry, Recording>? onTake = null, CancellationToken cancellationToken = default)
    {
        var entries = new List<BankEntry>();
        foreach (var line in lines)
            entries.Add(await PrepareLineAsync(line, onTake, cancellationToken));

        var manifest = new BankManifest(DateTimeOffset.Now, settings.SpeechModel, settings.Voice, settings.SpeechInstructions, entries);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, ManifestJson), cancellationToken);
        return entries;
    }

    /// <summary>Records one line and retakes it until every listener hears it, or the takes run out. Used for the bank and for lines improvised mid-call.</summary>
    public async Task<BankEntry> PrepareLineAsync(ScriptLine line, Action<BankEntry, Recording>? onTake = null, CancellationToken cancellationToken = default)
    {
        if (settings.CheckModels.Count == 0)
            throw new InvalidOperationException("Voice:CheckModels is empty; a line that no listener checked would pass by default.");
        if (settings.MaxTakes < 1)
            throw new InvalidOperationException("Voice:MaxTakes must be at least 1.");

        BankEntry entry;
        var take = 1;
        do
        {
            var recording = await studio.RecordAsync(line.Text, take, cancellationToken);
            entry = new BankEntry(line.Key, line.Text, take, recording.Path, recording.Audio.Length, await HearByEveryListenerAsync(recording, line, cancellationToken));
            onTake?.Invoke(entry, recording);
        }
        while (!entry.Passed && ++take <= settings.MaxTakes);

        return entry;
    }

    /// <summary>
    /// The prepared recordings, keyed by script line. Refuses a manifest that was made for a different voice
    /// setup or that still holds a line no listener could hear: a call must not open with recordings it cannot trust.
    /// </summary>
    public IReadOnlyDictionary<string, BankEntry> Load()
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"No voice bank at {Path.GetFullPath(manifestPath)}; run --voices first.");

        var manifest = JsonSerializer.Deserialize<BankManifest>(File.ReadAllText(manifestPath), ManifestJson)
            ?? throw new InvalidOperationException($"Unreadable voice bank at {Path.GetFullPath(manifestPath)}.");

        if (manifest.SpeechModel != settings.SpeechModel || manifest.Voice != settings.Voice || manifest.SpeechInstructions != settings.SpeechInstructions)
            throw new InvalidOperationException($"The voice bank was prepared for {manifest.SpeechModel}/{manifest.Voice}, the settings now say {settings.SpeechModel}/{settings.Voice}; run --voices again.");

        if (manifest.Entries.FirstOrDefault(entry => !entry.Passed) is { } failed)
            throw new InvalidOperationException($"Line '{failed.Key}' is in the bank but no listener heard it correctly; run --voices again.");

        if (manifest.Entries.FirstOrDefault(entry => !File.Exists(entry.RecordingPath)) is { } missing)
            throw new FileNotFoundException($"The recording for '{missing.Key}' is gone from {missing.RecordingPath}; run --voices again.");

        return manifest.Entries.ToDictionary(entry => entry.Key);
    }

    private async Task<IReadOnlyList<LineCheck>> HearByEveryListenerAsync(Recording recording, ScriptLine line, CancellationToken cancellationToken)
    {
        var checks = new List<LineCheck>();
        foreach (var model in settings.CheckModels)
        {
            var transcript = await studio.HearAsync(recording.Audio, "line.mp3", model, cancellationToken: cancellationToken);
            checks.Add(new LineCheck(model, transcript, TranscriptCheck.MissingTerms(transcript, line.RequiredTerms)));
        }

        return checks;
    }
}
