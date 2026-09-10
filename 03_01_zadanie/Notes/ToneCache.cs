using System.Text.Json;

namespace _03_01_zadanie.Notes;

/// <summary>
/// Verdicts already paid for, kept on disk between runs and keyed per model — a different
/// model is a different classifier and may disagree. The provider caches prompt prefixes on
/// its side; this is the same idea on ours, and it makes a re-run of the whole pipeline free.
/// </summary>
public sealed class ToneCache
{
    private readonly string _path;
    private readonly Dictionary<string, NoteTone> _entries;

    public ToneCache(string cacheDirectory, string model)
    {
        Directory.CreateDirectory(cacheDirectory);
        var safeModel = string.Concat(model.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '_'));
        _path = Path.Combine(cacheDirectory, $"tone-cache.{safeModel}.json");

        _entries = File.Exists(_path)
            ? JsonSerializer.Deserialize<Dictionary<string, NoteTone>>(File.ReadAllText(_path)) ?? new()
            : new();
    }

    public int Count => _entries.Count;

    public bool TryGet(string text, out NoteTone tone) => _entries.TryGetValue(text, out tone);

    public void Set(string text, NoteTone tone) => _entries[text] = tone;

    public void Save() =>
        File.WriteAllText(_path, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
}
