using System.IO.Compression;

namespace _03_01_zadanie.Sensors;

/// <summary>
/// Downloads, unpacks and loads the sensor archive. The extracted files are cached on disk so
/// every later mode runs offline; <c>--refresh</c> forces a fresh download.
/// </summary>
public sealed class SensorArchive(string archiveUrl, string cacheDirectory)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public string ArchivePath => Path.Combine(cacheDirectory, "sensors.zip");
    public string ExtractedPath => Path.Combine(cacheDirectory, "extracted");

    public async Task<int> EnsureDownloadedAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheDirectory);

        if (refresh || !File.Exists(ArchivePath))
        {
            Console.WriteLine($"Downloading {archiveUrl} ...");
            var bytes = await _http.GetByteArrayAsync(archiveUrl, cancellationToken);
            await File.WriteAllBytesAsync(ArchivePath, bytes, cancellationToken);
            Console.WriteLine($"  saved {bytes.Length:N0} bytes to {ArchivePath}");
        }

        if (refresh && Directory.Exists(ExtractedPath))
            Directory.Delete(ExtractedPath, recursive: true);

        if (!Directory.Exists(ExtractedPath))
        {
            ZipFile.ExtractToDirectory(ArchivePath, ExtractedPath);
            Console.WriteLine($"  extracted to {ExtractedPath}");
        }

        return Directory.GetFiles(ExtractedPath, "*.json", SearchOption.AllDirectories).Length;
    }

    /// <summary>
    /// Loads every reading, ordered by identifier. Files live flat in the archive but are read
    /// recursively so a future nested layout does not silently drop measurements.
    /// </summary>
    public IReadOnlyList<SensorReading> LoadAll()
    {
        if (!Directory.Exists(ExtractedPath))
            throw new InvalidOperationException($"No extracted sensor data at {ExtractedPath} — run with --fetch first.");

        return Directory.GetFiles(ExtractedPath, "*.json", SearchOption.AllDirectories)
            .Select(path => SensorReading.Parse(Path.GetFileNameWithoutExtension(path), File.ReadAllText(path)))
            .OrderBy(reading => reading.Id, StringComparer.Ordinal)
            .ToList();
    }
}
