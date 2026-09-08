using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace _02_05_zadanie.Map;

/// <summary>One cell of the sector grid, cropped strictly between the grid lines.</summary>
public sealed record MapSector(int Column, int Row, Rectangle Bounds, byte[] PngBytes)
{
    /// <summary>Label used when talking to the vision model and when naming cache files.</summary>
    public string Label => $"col{Column}-row{Row}";
}

public sealed record MapGrid(int Columns, int Rows, IReadOnlyList<MapSector> Sectors)
{
    public MapSector Sector(int column, int row) =>
        Sectors.First(s => s.Column == column && s.Row == row);
}

/// <summary>
/// Finds the sector grid drawn over the reconnaissance map and cuts the map along it.
/// Counting grid lines is exactly the step vision models get wrong, so it happens here:
/// the overlay is drawn in pure red, which no aerial photography content comes close to,
/// making a grid line a row or column where most pixels are strongly red.
/// </summary>
public static class MapGridDetector
{
    private const int MinRedChannel = 150;
    private const int MinRedDominance = 70;
    private const double MinLineCoverage = 0.60;
    private const int MaxLineGap = 3;

    public static MapGrid Detect(byte[] mapPng)
    {
        using var image = Image.Load<Rgb24>(mapPng);
        var red = BuildRedMask(image);

        var verticalLines = FindLines(red, image.Width, image.Height, scanColumns: true);
        var horizontalLines = FindLines(red, image.Width, image.Height, scanColumns: false);

        if (verticalLines.Count < 2 || horizontalLines.Count < 2)
            throw new InvalidOperationException(
                $"Grid detection failed — found {verticalLines.Count} vertical and {horizontalLines.Count} horizontal red lines, need at least 2 of each.");

        var columns = verticalLines.Count - 1;
        var rows = horizontalLines.Count - 1;

        var sectors = new List<MapSector>(columns * rows);
        for (var row = 1; row <= rows; row++)
        {
            for (var column = 1; column <= columns; column++)
            {
                // Crop strictly between the lines so no red pixel bleeds into a tile and
                // distracts the vision model.
                var x0 = verticalLines[column - 1].End + 1;
                var x1 = verticalLines[column].Start - 1;
                var y0 = horizontalLines[row - 1].End + 1;
                var y1 = horizontalLines[row].Start - 1;
                var bounds = new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);

                using var crop = image.Clone(ctx => ctx.Crop(bounds));
                using var ms = new MemoryStream();
                crop.SaveAsPng(ms);
                sectors.Add(new MapSector(column, row, bounds, ms.ToArray()));
            }
        }

        return new MapGrid(columns, rows, sectors);
    }

    /// <summary>True where a pixel is red enough to belong to the overlay rather than to the photo.</summary>
    public static bool[,] BuildRedMask(Image<Rgb24> image)
    {
        var mask = new bool[image.Height, image.Width];
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var pixels = accessor.GetRowSpan(y);
                for (var x = 0; x < pixels.Length; x++)
                {
                    var p = pixels[x];
                    mask[y, x] = p.R > MinRedChannel && p.R - Math.Max(p.G, p.B) > MinRedDominance;
                }
            }
        });
        return mask;
    }

    private sealed record Line(int Start, int End);

    private static List<Line> FindLines(bool[,] red, int width, int height, bool scanColumns)
    {
        var outerCount = scanColumns ? width : height;
        var innerCount = scanColumns ? height : width;
        var minCovered = (int)(innerCount * MinLineCoverage);

        var hits = new List<int>();
        for (var outer = 0; outer < outerCount; outer++)
        {
            var covered = 0;
            for (var inner = 0; inner < innerCount; inner++)
                if (scanColumns ? red[inner, outer] : red[outer, inner])
                    covered++;

            if (covered >= minCovered)
                hits.Add(outer);
        }

        // A drawn line is several pixels thick; collapse each run of hits into one line.
        var lines = new List<Line>();
        for (var i = 0; i < hits.Count;)
        {
            var start = hits[i];
            var end = start;
            while (i + 1 < hits.Count && hits[i + 1] - end <= MaxLineGap)
                end = hits[++i];

            lines.Add(new Line(start, end));
            i++;
        }

        return lines;
    }
}
