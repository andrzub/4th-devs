namespace _05_01_zadanie.Capture;

/// <summary>
/// One collection run on disk: every hub reply verbatim (<c>capture-NNN.json</c>) plus each
/// attachment decoded next to it under an extension taken from the declared MIME type.
/// Everything later in the pipeline reads from here, so the listening session is paid for once.
/// </summary>
public sealed class CaptureStore(string runDirectory)
{
    private const string CapturePrefix = "capture-";

    public string RunDirectory { get; } = runDirectory;

    public static CaptureStore CreateRun(string cacheDirectory)
    {
        var directory = Path.Combine(cacheDirectory, $"run-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(directory);
        return new CaptureStore(directory);
    }

    /// <summary>The most recent run directory, used when triage is started without a path.</summary>
    public static CaptureStore Latest(string cacheDirectory)
    {
        var latest = Directory.Exists(cacheDirectory)
            ? Directory.GetDirectories(cacheDirectory, "run-*").OrderBy(path => path, StringComparer.Ordinal).LastOrDefault()
            : null;

        return new CaptureStore(latest ?? throw new DirectoryNotFoundException($"No collection run under {Path.GetFullPath(cacheDirectory)}; run --collect first."));
    }

    public void SaveStartReply(string body) => File.WriteAllText(Path.Combine(RunDirectory, "start.json"), body);

    public void SaveFinalReply(string body) => File.WriteAllText(Path.Combine(RunDirectory, "final.json"), body);

    public Capture Save(int number, string body)
    {
        var rawPath = Path.Combine(RunDirectory, $"{CapturePrefix}{number:D3}.json");
        File.WriteAllText(rawPath, body);

        var capture = Capture.Parse(number, body, rawPath);
        if (capture.Content is { } content)
            File.WriteAllBytes(AttachmentPath(number, capture.Meta ?? FileSniffer.Sniff(content)), content);

        return capture;
    }

    /// <summary>Decoded files carry their own infix, so a JSON attachment never lands on the raw reply's path.</summary>
    public string AttachmentPath(int number, string mime) =>
        Path.Combine(RunDirectory, $"{CapturePrefix}{number:D3}.attachment.{FileSniffer.Extension(mime)}");

    public IReadOnlyList<Capture> Load() =>
        Directory.GetFiles(RunDirectory, $"{CapturePrefix}???.json")
            .Order(StringComparer.Ordinal)
            .Select(path => Capture.Parse(int.Parse(Path.GetFileNameWithoutExtension(path)[CapturePrefix.Length..]), File.ReadAllText(path), path))
            .ToList();
}
