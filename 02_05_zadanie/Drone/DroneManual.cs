using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace _02_05_zadanie.Drone;

/// <summary>
/// Fetches the drone's API documentation and renders it as plain text for the agent's prompt.
/// The manual is deliberately full of colliding method names, so it travels to the model as
/// close to the original as HTML allows — a paraphrase of mine would quietly resolve exactly
/// the ambiguities the mission is built on.
/// </summary>
public sealed partial class DroneManual
{
    private const string PlaceholderPrefix = "[[PRE";
    private const string PlaceholderSuffix = "]]";

    private DroneManual(string text, string sourceUrl)
    {
        Text = text;
        SourceUrl = sourceUrl;
    }

    public string Text { get; }
    public string SourceUrl { get; }

    public static async Task<DroneManual> LoadAsync(string url, string cacheDirectory, bool refresh = false, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheDirectory);
        var htmlPath = Path.Combine(cacheDirectory, "drone-manual.html");
        var textPath = Path.Combine(cacheDirectory, "drone-manual.txt");

        string html;
        if (!refresh && File.Exists(htmlPath))
        {
            html = await File.ReadAllTextAsync(htmlPath, cancellationToken);
        }
        else
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(1) };
            html = await http.GetStringAsync(url, cancellationToken);
            await File.WriteAllTextAsync(htmlPath, html, cancellationToken);
        }

        var text = ToPlainText(html);
        await File.WriteAllTextAsync(textPath, text, cancellationToken);
        return new DroneManual(text, url);
    }

    /// <summary>
    /// Turns the documentation page into text that keeps its structure: the method reference is
    /// a table, and a naive tag strip would mash every row into one unreadable paragraph.
    /// Rows become one line of pipe-separated cells, and the JSON examples keep their line breaks.
    /// </summary>
    public static string ToPlainText(string html)
    {
        var work = HeadRegex().Replace(html, "");

        // Park the preformatted examples so the whitespace clean-up below cannot reflow them.
        var preserved = new List<string>();
        work = PreRegex().Replace(work, match =>
        {
            preserved.Add(WebUtility.HtmlDecode(StripTags(match.Groups[1].Value)).Trim('\r', '\n'));
            return string.Concat("\n", PlaceholderPrefix, preserved.Count - 1, PlaceholderSuffix, "\n");
        });

        work = TableRowRegex().Replace(work, match =>
        {
            var cells = TableCellRegex().Matches(match.Groups[1].Value)
                .Select(cell => CollapseSpaces(WebUtility.HtmlDecode(StripTags(cell.Groups[1].Value))))
                .Where(cell => cell.Length > 0);
            return "\n" + string.Join(" | ", cells);
        });

        work = ListItemRegex().Replace(work, "\n- ");
        work = BlockEndRegex().Replace(work, "\n");
        work = LineBreakRegex().Replace(work, " ");
        work = StripTags(work);
        work = WebUtility.HtmlDecode(work);

        var text = Reflow(work);
        for (var i = 0; i < preserved.Count; i++)
            text = text.Replace(string.Concat(PlaceholderPrefix, i, PlaceholderSuffix), preserved[i]);

        return text.Trim();
    }

    /// <summary>Trims every line, squeezes the spaces left behind by removed tags, and keeps at most one blank line.</summary>
    private static string Reflow(string value)
    {
        var result = new StringBuilder();
        var blankPending = false;

        foreach (var raw in value.Split('\n'))
        {
            var line = CollapseSpaces(raw);
            if (line.Length == 0)
            {
                blankPending = result.Length > 0;
                continue;
            }

            if (blankPending)
                result.AppendLine();

            blankPending = false;
            result.AppendLine(line);
        }

        return result.ToString();
    }

    /// <summary>Tags become a space, not nothing: two inline elements written back to back are separate words.</summary>
    private static string StripTags(string value) => TagRegex().Replace(value, " ");

    /// <summary>Squeezes runs of spaces and drops the one a removed closing tag left before punctuation.</summary>
    private static string CollapseSpaces(string value) =>
        SpaceBeforePunctuationRegex().Replace(WhitespaceRegex().Replace(value, " "), "$1").Trim();

    [GeneratedRegex(@"<head\b[^>]*>.*?</head>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HeadRegex();

    [GeneratedRegex(@"<pre\b[^>]*>(.*?)</pre>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PreRegex();

    [GeneratedRegex(@"<tr\b[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TableRowRegex();

    [GeneratedRegex(@"<t[dh]\b[^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TableCellRegex();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"</(p|h1|h2|h3|h4|ul|ol|li|section|div|table|thead|tbody)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@" +([,.;:)\]])")]
    private static partial Regex SpaceBeforePunctuationRegex();
}
