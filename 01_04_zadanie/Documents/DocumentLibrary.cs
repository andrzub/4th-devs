namespace _01_04_zadanie.Documents;

/// <summary>
/// Resolves the file names the agent asks for into actual bytes fetched from the SPK
/// documentation host. This is the layer that turns a plain file reference passed between
/// tool calls into either text or a base64 data URL, so the agent never handles raw content itself.
/// Everything downloaded is cached on disk, which keeps repeated runs cheap and offline-friendly.
/// </summary>
public sealed class DocumentLibrary
{
    private static readonly Dictionary<string, string> MediaTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"]  = "image/png",
        [".jpg"]  = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"]  = "image/gif",
        [".webp"] = "image/webp"
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _cacheDirectory;

    public DocumentLibrary(string baseUrl, string cacheDirectory)
    {
        _baseUrl = baseUrl.TrimEnd('/') + "/";
        _cacheDirectory = cacheDirectory;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        Directory.CreateDirectory(_cacheDirectory);
    }

    public static bool IsImage(string fileName) => MediaTypesByExtension.ContainsKey(Path.GetExtension(fileName));

    public async Task<string> ReadTextAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(fileName, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Returns the file as a complete <c>data:</c> URL, ready to be attached to a vision request.
    /// </summary>
    public async Task<string> ReadAsDataUrlAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBytesAsync(fileName, cancellationToken);
        var mediaType = MediaTypesByExtension.TryGetValue(Path.GetExtension(fileName), out var type) ? type : "application/octet-stream";

        return $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
    }

    public async Task<byte[]> ReadBytesAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var safeName = Sanitize(fileName);
        var cachePath = Path.Combine(_cacheDirectory, safeName);

        if (File.Exists(cachePath))
            return await File.ReadAllBytesAsync(cachePath, cancellationToken);

        var bytes = await _http.GetByteArrayAsync(_baseUrl + safeName, cancellationToken);
        await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);

        return bytes;
    }

    /// <summary>
    /// The file name comes from the model, so it is constrained to a bare name inside the
    /// documentation directory — no traversal, no absolute URLs, no subdirectories.
    /// </summary>
    private static string Sanitize(string fileName)
    {
        var trimmed = fileName.Trim().TrimStart('/');

        if (trimmed.Length == 0)
            throw new ArgumentException("Document name is empty.", nameof(fileName));

        if (trimmed.Contains("..") || trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.Contains(':'))
            throw new ArgumentException($"Document name '{fileName}' must be a bare file name from the documentation directory.", nameof(fileName));

        return trimmed;
    }
}
