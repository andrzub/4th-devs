using System.Text;
using System.Text.Json;
using _02_02_zadanie.Llm;

namespace _02_02_zadanie.Board;

/// <summary>
/// Turns a board PNG into a text description the agent can reason about.
/// The board is sliced into nine tile crops that travel together in one vision request —
/// crops remove grid-coordinate ambiguity, batching respects the scarce daily quota.
/// Tile crops are also saved to the cache directory for manual inspection.
/// </summary>
public class TileVisionService
{
    // All nine tiles travel in ONE request: the Gemini preview free tier allows only
    // 20 requests per day, so per-tile calls would burn the whole quota on two board reads.
    private const string BoardPrompt =
        "You receive nine images. They are the nine tiles of a 3x3 wiring puzzle board, " +
        "in row-major order: 1x1, 1x2, 1x3, 2x1, 2x2, 2x3, 3x1, 3x2, 3x3 (row x column, row 1 at the top). " +
        "Each tile may contain a thick black cable that exits through the middle of its edges: " +
        "top, right, bottom, left. Ignore thin hand-drawn border lines, background texture and " +
        "anything that is not a thick black cable. For EACH tile report ONLY the edges where a " +
        "thick cable actually reaches the border of that image; use an empty list when no cable exits. " +
        "Respond with JSON only, no prose, exactly nine keys: " +
        "{\"1x1\": [\"top\", \"right\"], \"1x2\": [], ...}";

    private readonly ILlmClient _visionClient;
    private readonly string _cacheDirectory;

    public TileVisionService(ILlmClient visionClient, string cacheDirectory)
    {
        _visionClient = visionClient;
        _cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(cacheDirectory);
    }

    public async Task<string> DescribeBoardAsync(byte[] boardPng, string cachePrefix, CancellationToken cancellationToken = default)
    {
        var tiles = BoardImageSlicer.Slice(boardPng);

        var dataUrls = new List<string>(tiles.Count);
        foreach (var tile in tiles)
        {
            await File.WriteAllBytesAsync(Path.Combine(_cacheDirectory, $"{cachePrefix}_{tile.Label}.png"), tile.PngBytes, cancellationToken);
            dataUrls.Add("data:image/png;base64," + Convert.ToBase64String(tile.PngBytes));
        }

        // No MaxTokens: Gemini 3 Flash is a thinking model and reasoning tokens count
        // against the limit, so a small cap truncates the reply mid-answer.
        var response = await _visionClient.CompleteAsync(new LlmRequest
        {
            Messages = [Message.UserWithImages(BoardPrompt, dataUrls)],
            Temperature = 0
        }, cancellationToken);

        var edgesByTile = ParseBoardReply(response.Content ?? response.ContentRaw ?? "");

        var description = new StringBuilder();
        description.AppendLine("Tiles are addressed RxC (row 1-3 from top, column 1-3 from left).");
        description.AppendLine("Listed edges are where a cable exits the tile: U=top, R=right, D=bottom, L=left.");
        foreach (var tile in tiles)
        {
            if (!edgesByTile.TryGetValue(tile.Label, out var edges))
                throw new InvalidOperationException($"Vision model reply is missing tile '{tile.Label}'.");
            description.AppendLine($"{tile.Label}: [{string.Join(",", edges)}] ({DescribeShape(edges)})");
        }

        return description.ToString();
    }

    /// <summary>
    /// Extracts the nine per-tile edge arrays from the model reply, tolerating stray prose
    /// or code fences. Normalises edges to single letters U/R/D/L ordered clockwise from top.
    /// </summary>
    private static Dictionary<string, IReadOnlyList<string>> ParseBoardReply(string modelReply)
    {
        var start = modelReply.IndexOf('{');
        var end = modelReply.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException($"Vision model returned no JSON object (likely truncated reply): '{modelReply}'");

        using var doc = JsonDocument.Parse(modelReply[start..(end + 1)]);

        var result = new Dictionary<string, IReadOnlyList<string>>();
        var clockwiseOrder = new[] { "U", "R", "D", "L" };
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            var edges = property.Value.EnumerateArray()
                .Select(e => e.GetString()?.Trim().ToLowerInvariant() switch
                {
                    "top" or "up" or "u" => "U",
                    "right" or "r" => "R",
                    "bottom" or "down" or "d" => "D",
                    "left" or "l" => "L",
                    var other => throw new InvalidOperationException($"Vision model returned unknown edge '{other}' for tile '{property.Name}'.")
                })
                .Distinct()
                .ToList();

            result[property.Name.Trim()] = clockwiseOrder.Where(edges.Contains).ToList();
        }

        return result;
    }

    private static string DescribeShape(IReadOnlyList<string> edges) => edges.Count switch
    {
        0 => "no cable exits",
        1 => "dead end",
        2 when (edges.Contains("U") && edges.Contains("D")) || (edges.Contains("L") && edges.Contains("R")) => "straight",
        2 => "corner",
        3 => "T-junction",
        _ => "cross"
    };
}
