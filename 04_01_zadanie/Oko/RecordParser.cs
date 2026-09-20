using System.Net;
using System.Text.RegularExpressions;

namespace _04_01_zadanie.Oko;

/// <summary>
/// Turns the console's HTML into records. The markup is regular enough to read with patterns:
/// every list entry is an anchor to "/&lt;page&gt;/&lt;id&gt;" followed by its own metadata, and
/// every detail view carries the page in an eyebrow, the title in a heading and the body in a
/// single paragraph.
/// </summary>
public static partial class RecordParser
{
    [GeneratedRegex(@"href=""/(?<page>incydenty|notatki|zadania|uzytkownicy)/(?<id>[0-9a-fA-F]{32})""", RegexOptions.IgnoreCase)]
    private static partial Regex EntryLinkRegex();

    [GeneratedRegex(@"<strong[^>]*>(?<text>.*?)</strong>", RegexOptions.Singleline)]
    private static partial Regex StrongRegex();

    [GeneratedRegex(@"<p[^>]*>(?<text>.*?)</p>", RegexOptions.Singleline)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex(@"class=""[^""]*\b(metric|pill)\b[^""]*""[^>]*>(?<text>.*?)<", RegexOptions.Singleline)]
    private static partial Regex MetricRegex();

    [GeneratedRegex(@"<p[^>]*class=""[^""]*\beyebrow\b[^""]*""[^>]*>(?<text>.*?)</p>", RegexOptions.Singleline)]
    private static partial Regex EyebrowRegex();

    [GeneratedRegex(@"<h2[^>]*class=""[^""]*\bhero-title\b[^""]*""[^>]*>(?<text>.*?)</h2>", RegexOptions.Singleline)]
    private static partial Regex HeroTitleRegex();

    [GeneratedRegex(@"<p[^>]*class=""[^""]*\bdetail-content\b[^""]*""[^>]*>(?<text>.*?)</p>", RegexOptions.Singleline)]
    private static partial Regex DetailContentRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"name=""(?<field>login|password|access_key)""")]
    private static partial Regex LoginFieldRegex();

    /// <summary>
    /// Reads a listing. Each entry spans from its own link to the next one, because a task's status
    /// sits next to the link rather than inside it.
    /// </summary>
    public static IReadOnlyList<OkoRecord> ParseListing(string page, string html)
    {
        var matches = EntryLinkRegex().Matches(html);
        var records = new List<OkoRecord>();

        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : html.Length;
            var block = html[start..end];

            var title = Clean(StrongRegex().Match(block).Groups["text"].Value);
            var paragraphs = ParagraphRegex().Matches(block).Select(m => Clean(m.Groups["text"].Value)).Where(text => text.Length > 0).ToList();
            var metrics = MetricRegex().Matches(block).Select(m => Clean(m.Groups["text"].Value)).Where(text => text.Length > 0).ToList();

            records.Add(new OkoRecord(
                Page: matches[i].Groups["page"].Value.ToLowerInvariant(),
                Id: matches[i].Groups["id"].Value.ToLowerInvariant(),
                Title: title,
                Summary: paragraphs.FirstOrDefault() ?? string.Empty,
                Meta: string.Join(" | ", metrics.Where(text => !IsStatusWord(text))),
                Done: ReadDone(metrics)));
        }

        return records;
    }

    public static OkoRecord ParseDetail(string page, string id, string html)
    {
        var metrics = MetricRegex().Matches(html).Select(m => Clean(m.Groups["text"].Value)).Where(text => text.Length > 0).ToList();

        return new OkoRecord(
            Page: Clean(EyebrowRegex().Match(html).Groups["text"].Value) is { Length: > 0 } eyebrow ? eyebrow.ToLowerInvariant() : page,
            Id: id,
            Title: Clean(HeroTitleRegex().Match(html).Groups["text"].Value),
            Meta: string.Join(" | ", metrics.Where(text => !IsStatusWord(text))),
            Content: string.Join(Environment.NewLine, DetailContentRegex().Matches(html).Select(m => Clean(m.Groups["text"].Value)).Where(text => text.Length > 0)),
            Done: ReadDone(metrics));
    }

    /// <summary>True when the console answered with the sign-in form instead of the page asked for.</summary>
    public static bool IsLoginPage(string html) => LoginFieldRegex().Matches(html).Count >= 3;

    /// <summary>
    /// "niewykonane" contains "wykonane", so the negative form is tested first — the other order
    /// reads every pending task as finished.
    /// </summary>
    private static bool? ReadDone(IEnumerable<string> metrics)
    {
        foreach (var metric in metrics)
        {
            if (metric.Contains("niewykonane", StringComparison.OrdinalIgnoreCase))
                return false;
            if (metric.Contains("wykonane", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return null;
    }

    private static bool IsStatusWord(string text) => text.Contains("wykonane", StringComparison.OrdinalIgnoreCase);

    private static string Clean(string html) => WebUtility.HtmlDecode(TagRegex().Replace(html, " ")).ReplaceLineEndings(" ").Replace('\t', ' ').Trim() is { } text
        ? string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        : string.Empty;
}
