using System.Text;
using System.Text.Json;
using _02_05_zadanie.Llm;

namespace _02_05_zadanie.Map;

public sealed record LocalMapAnalysis(MapGrid Grid, WaterSignalReport Water);

public sealed record DamLocation(int Column, int Row, WaterSignalReport Water, string VisionLabel, string VisionReason)
{
    public string Label => $"col{Column}-row{Row}";

    public string Render() =>
        $"""
         Dam sector: column {Column}, row {Row}.
           pixel analysis : {Water.Strongest?.Label ?? "none"} ({Water.StrongestCount} boosted-water px, {Water.StrongestShare:P1} of all hits)
           vision model   : {VisionLabel} — {VisionReason}
         """;
}

public sealed class DamMismatchException(string message) : Exception(message);

/// <summary>
/// Answers the single question the mission cannot recover from getting wrong: which sector
/// holds the dam. Two independent readings must agree — a pixel analysis of the boosted
/// water colour and a vision model looking at the cropped sectors — because the drone carries
/// one charge and drops it once. Neither reading is told what the other found.
/// </summary>
public sealed class DamLocator(ILlmClient visionClient, string cacheDirectory)
{
    private const string VisionPrompt =
        """
        You are looking at the sectors of one aerial reconnaissance photo of a derelict power plant site.
        The images are the sectors of a grid, given in this order (row by row, top to bottom):
        {LABELS}

        Exactly one sector contains a DAM: a concrete barrage or weir built across water, recognisable by
        sluice gates or spillway openings, a walkway or bridge running along its crest, and water impounded
        against it. Long straight concrete walls, empty channels, dried-out basins and flooded building
        foundations are NOT dams.

        Answer with JSON only, no prose, no code fence:
        {"dam_sector": "<one label from the list above>", "reason": "<what you see there, one short sentence>"}
        """;

    /// <summary>
    /// The half of the reading that needs no model, so the modes that only inspect the map —
    /// and the hand-flown submission — work without a vision provider configured.
    /// </summary>
    public static LocalMapAnalysis AnalyzeLocally(byte[] mapPng)
    {
        var grid = MapGridDetector.Detect(mapPng);
        var water = WaterSignalAnalyzer.Analyze(mapPng, grid);
        return new LocalMapAnalysis(grid, water);
    }

    public static async Task SaveSectorsAsync(MapGrid grid, string cacheDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheDirectory);
        foreach (var sector in grid.Sectors)
            await File.WriteAllBytesAsync(Path.Combine(cacheDirectory, $"sector-{sector.Label}.png"), sector.PngBytes, cancellationToken);
    }

    public async Task<DamLocation> LocateAsync(byte[] mapPng, CancellationToken cancellationToken = default)
    {
        var (grid, water) = AnalyzeLocally(mapPng);
        await SaveSectorsAsync(grid, cacheDirectory, cancellationToken);

        Console.WriteLine($"Grid detected: {grid.Columns} columns x {grid.Rows} rows.");
        Console.WriteLine(water.Render());

        if (!water.IsConclusive)
            throw new DamMismatchException(
                $"The boosted-water signal is not conclusive, so the dam sector cannot be trusted to code alone.{Environment.NewLine}{water.Render()}");

        var (visionLabel, visionReason) = await AskVisionAsync(grid, cancellationToken);
        Console.WriteLine($"Vision model says: {visionLabel} — {visionReason}");

        var strongest = water.Strongest!;
        if (!string.Equals(visionLabel, strongest.Label, StringComparison.OrdinalIgnoreCase))
            throw new DamMismatchException(
                $"""
                 The two readings of the map disagree, so the run stops before anything is dropped.
                   pixel analysis: {strongest.Label}
                   vision model  : {visionLabel} — {visionReason}
                 Inspect the sector crops in '{cacheDirectory}' and decide manually.
                 """);

        return new DamLocation(strongest.Column, strongest.Row, water, visionLabel, visionReason);
    }

    private async Task<(string Label, string Reason)> AskVisionAsync(MapGrid grid, CancellationToken cancellationToken)
    {
        var labels = new StringBuilder();
        foreach (var sector in grid.Sectors)
            labels.AppendLine($"  {sector.Label}");

        var dataUrls = grid.Sectors
            .Select(s => "data:image/png;base64," + Convert.ToBase64String(s.PngBytes))
            .ToList();

        // Every sector travels in ONE request: the free tier is measured in requests per day,
        // and asking per sector would spend a dozen of them on a single map reading.
        // No MaxTokens either — Gemini 3 is a thinking model and reasoning tokens count against
        // the limit, so a small cap truncates the reply before the answer appears.
        var response = await visionClient.CompleteAsync(new LlmRequest
        {
            Messages = [Message.UserWithImages(VisionPrompt.Replace("{LABELS}", labels.ToString().TrimEnd()), dataUrls)],
            Temperature = 0
        }, cancellationToken);

        var reply = response.Content ?? response.ContentRaw ?? "";
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new DamMismatchException($"Vision model returned no JSON object (likely a truncated reply): '{reply}'");

        using var doc = JsonDocument.Parse(reply[start..(end + 1)]);
        var label = doc.RootElement.TryGetProperty("dam_sector", out var el) ? el.GetString()?.Trim() ?? "" : "";
        var reason = doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString()?.Trim() ?? "" : "";

        if (!grid.Sectors.Any(s => string.Equals(s.Label, label, StringComparison.OrdinalIgnoreCase)))
            throw new DamMismatchException($"Vision model answered with '{label}', which is not one of the {grid.Sectors.Count} sector labels.");

        return (label, reason);
    }
}
