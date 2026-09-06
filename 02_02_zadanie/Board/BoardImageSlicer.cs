using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace _02_02_zadanie.Board;

/// <summary>
/// Locates the 3x3 grid inside a board PNG and slices it into nine tile images.
/// The board and the target schema differ in resolution and margins, so the grid is
/// detected instead of hardcoded: grid border lines produce the longest continuous
/// dark-pixel runs in their row/column, while the title text and power plant icons
/// only produce short ones.
/// </summary>
public static class BoardImageSlicer
{
    private const byte DarkThreshold = 100;
    private const double MinRunFraction = 0.35;
    private const int TileUpscaleFactor = 3;

    public record Tile(int Row, int Column, byte[] PngBytes)
    {
        public string Label => $"{Row}x{Column}";
    }

    public static IReadOnlyList<Tile> Slice(byte[] boardPng)
    {
        using var image = Image.Load<Rgba32>(boardPng);
        var dark = BuildDarkMask(image);

        var gridColumns = FindLongRunIndices(dark, vertical: true, minRun: (int)(image.Height * MinRunFraction));
        var gridRows = FindLongRunIndices(dark, vertical: false, minRun: (int)(image.Width * MinRunFraction));

        if (gridColumns.Count == 0 || gridRows.Count == 0)
            throw new InvalidOperationException("Grid detection failed — no long dark runs found in the image.");

        var left = gridColumns.Min();
        var right = gridColumns.Max();
        var top = gridRows.Min();
        var bottom = gridRows.Max();

        var tiles = new List<Tile>(9);
        for (var row = 1; row <= 3; row++)
        {
            for (var column = 1; column <= 3; column++)
            {
                var x0 = left + (right - left) * (column - 1) / 3;
                var x1 = left + (right - left) * column / 3;
                var y0 = top + (bottom - top) * (row - 1) / 3;
                var y1 = top + (bottom - top) * row / 3;

                using var tileImage = image.Clone(ctx => ctx
                    .Crop(new Rectangle(x0, y0, x1 - x0, y1 - y0))
                    .Resize((x1 - x0) * TileUpscaleFactor, (y1 - y0) * TileUpscaleFactor));

                using var ms = new MemoryStream();
                tileImage.SaveAsPng(ms);
                tiles.Add(new Tile(row, column, ms.ToArray()));
            }
        }

        return tiles;
    }

    private static bool[,] BuildDarkMask(Image<Rgba32> image)
    {
        var mask = new bool[image.Width, image.Height];
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var px = row[x];
                    var luminance = (px.R * 299 + px.G * 587 + px.B * 114) / 1000;
                    mask[x, y] = luminance < DarkThreshold;
                }
            }
        });
        return mask;
    }

    private static List<int> FindLongRunIndices(bool[,] dark, bool vertical, int minRun)
    {
        var width = dark.GetLength(0);
        var height = dark.GetLength(1);
        var outerCount = vertical ? width : height;
        var innerCount = vertical ? height : width;

        var result = new List<int>();
        for (var outer = 0; outer < outerCount; outer++)
        {
            var longestRun = 0;
            var currentRun = 0;
            for (var inner = 0; inner < innerCount; inner++)
            {
                var isDark = vertical ? dark[outer, inner] : dark[inner, outer];
                currentRun = isDark ? currentRun + 1 : 0;
                longestRun = Math.Max(longestRun, currentRun);
            }

            if (longestRun >= minRun)
                result.Add(outer);
        }

        return result;
    }
}
