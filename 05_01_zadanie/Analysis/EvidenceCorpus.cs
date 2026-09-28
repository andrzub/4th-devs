using System.Text;
using System.Text.RegularExpressions;
using _05_01_zadanie.Media;
using _05_01_zadanie.Routing;

namespace _05_01_zadanie.Analysis;

/// <summary>
/// Everything the analyst is allowed to read, as tagged sections that name their capture. It is also
/// the reference the validator checks quotes against, so a quote the model invents has nowhere to match.
/// </summary>
public sealed partial class EvidenceCorpus
{
    private readonly List<(string Tag, string Attributes, string Body)> _sections = [];

    public static EvidenceCorpus Build(IReadOnlyList<RoutedCapture> routed, IReadOnlyList<MediaReading> readings)
    {
        var corpus = new EvidenceCorpus();
        foreach (var item in routed)
        {
            var id = $"#{item.Capture.Number:D3}";
            switch (item.Route)
            {
                case Route.Speech:
                    corpus._sections.Add(("speech", $"id=\"{id}\"", item.LocalText!));
                    break;
                case Route.Morse:
                    corpus._sections.Add(("morse", $"id=\"{id}\" note=\"decoded by code from Ti/Ta syllables\"", item.LocalText!));
                    break;
                case Route.Document:
                    corpus._sections.Add(("document", $"id=\"{id}\" type=\"{item.Capture.Meta}\"", item.LocalText!));
                    break;
                case Route.Audio or Route.Image when readings.FirstOrDefault(r => r.CaptureNumber == item.Capture.Number) is { } reading:
                    var tag = item.Route == Route.Audio ? "audio-transcript" : "image-reading";
                    corpus._sections.Add((tag, $"id=\"{id}\" model=\"{reading.Model}\"", reading.Text));
                    break;
                case Route.Audio or Route.Image:
                    corpus._sections.Add(("unread-media", $"id=\"{id}\"", $"{item.Reason}; not interpreted in this run."));
                    break;
            }
        }

        var noise = routed.Where(item => item.Route == Route.Noise).ToList();
        if (noise.Count > 0)
        {
            var ids = string.Join(",", noise.Select(item => $"#{item.Capture.Number:D3}"));
            var fragments = NoiseFilter.DistinctFragments(noise.Select(item => item.Capture.Transcription!));
            corpus._sections.Add(("static-fragments", $"from=\"{ids}\" note=\"distinct speech fragments heard through static, deduplicated by code\"", string.Join(Environment.NewLine, fragments)));
        }

        return corpus;
    }

    public int SectionCount => _sections.Count;

    public string Render()
    {
        var output = new StringBuilder();
        foreach (var (tag, attributes, body) in _sections)
            output.AppendLine($"<{tag} {attributes}>").AppendLine(body).AppendLine($"</{tag}>");
        return output.ToString();
    }

    /// <summary>True when the quote occurs in some section, ignoring case, whitespace runs and quote-mark styles.</summary>
    public bool Contains(string quote)
    {
        var needle = Normalize(quote);
        return needle.Length > 0 && _sections.Any(section => Normalize(section.Body).Contains(needle, StringComparison.Ordinal));
    }

    private static string Normalize(string text) =>
        Whitespace().Replace(text.ToLowerInvariant().Replace('“', '"').Replace('”', '"').Replace('„', '"').Replace('’', '\''), " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
