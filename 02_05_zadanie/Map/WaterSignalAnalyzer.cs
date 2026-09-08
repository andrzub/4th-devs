using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace _02_05_zadanie.Map;

public sealed record WaterSignalReport(
    IReadOnlyDictionary<string, int> CountsBySector,
    MapSector? Strongest,
    int StrongestCount,
    double StrongestShare)
{
    /// <summary>
    /// The briefing says the water by the dam had its colour intensity boosted on purpose,
    /// so a real signal is not a narrow win — it is one sector holding nearly every hit.
    /// </summary>
    public bool IsConclusive => Strongest is not null && StrongestCount >= MinPixelsForSignal && StrongestShare >= MinShareForSignal;

    public const int MinPixelsForSignal = 500;
    public const double MinShareForSignal = 0.90;

    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Boosted-water pixels per sector:");
        foreach (var (label, count) in CountsBySector.OrderByDescending(e => e.Value))
            sb.AppendLine($"  {label}: {count}");

        sb.AppendLine(Strongest is null
            ? "  no sector carries a boosted-water signal"
            : $"  strongest: {Strongest.Label} with {StrongestCount} px ({StrongestShare:P1} of all hits), conclusive: {IsConclusive}");

        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Locates the artificially saturated water on the map. Everything in an aerial photo of a
/// derelict site is muted — greens, greys, browns, and the standing water in the pools is
/// murky green. The water at the dam was deliberately boosted, so a strongly saturated
/// blue-green pixel is a marker rather than scenery, and counting those per sector answers
/// the one question that must not be guessed.
/// </summary>
public static class WaterSignalAnalyzer
{
    private const int MinCyanDominance = 25;
    private const double MinSaturation = 0.35;
    private const int MinBrightness = 60;

    public static WaterSignalReport Analyze(byte[] mapPng, MapGrid grid)
    {
        using var image = Image.Load<Rgb24>(mapPng);

        var counts = grid.Sectors.ToDictionary(s => s.Label, _ => 0);
        var bySector = grid.Sectors.ToDictionary(s => s.Label, s => s);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var pixels = accessor.GetRowSpan(y);
                for (var x = 0; x < pixels.Length; x++)
                {
                    if (!IsBoostedWater(pixels[x]))
                        continue;

                    var sector = grid.Sectors.FirstOrDefault(s => s.Bounds.Contains(x, y));
                    if (sector is not null)
                        counts[sector.Label]++;
                }
            }
        });

        var total = counts.Values.Sum();
        var winner = counts.OrderByDescending(e => e.Value).First();

        return new WaterSignalReport(
            counts,
            winner.Value > 0 ? bySector[winner.Key] : null,
            winner.Value,
            total > 0 ? (double)winner.Value / total : 0);
    }

    private static bool IsBoostedWater(Rgb24 pixel)
    {
        var max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
        if (max <= MinBrightness)
            return false;

        if (pixel.B < pixel.R + MinCyanDominance || pixel.G < pixel.R + MinCyanDominance)
            return false;

        var min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
        return (double)(max - min) / max > MinSaturation;
    }
}
