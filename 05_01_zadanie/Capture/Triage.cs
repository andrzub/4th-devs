using System.Text;

namespace _05_01_zadanie.Capture;

/// <summary>
/// A model-free census of a collection run: how much of it is text, files or nothing, which file
/// types arrived and whether their labels match their bytes. The router is designed from this.
/// </summary>
public static class Triage
{
    private const int PreviewLength = 160;

    public static string Render(IReadOnlyList<Capture> captures)
    {
        var report = new StringBuilder();
        var transcriptions = captures.Where(c => c.Kind == CaptureKind.Transcription).ToList();
        var attachments = captures.Where(c => c.Kind == CaptureKind.Attachment).ToList();
        var empty = captures.Where(c => c.Kind == CaptureKind.Empty).ToList();

        report.AppendLine($"Captures: {captures.Count}  (transcriptions {transcriptions.Count}, attachments {attachments.Count}, empty {empty.Count})");
        report.AppendLine($"Codes: {string.Join(", ", captures.GroupBy(c => c.Code?.ToString() ?? "-").Select(g => $"{g.Key} x{g.Count()}"))}");
        report.AppendLine($"Messages: {string.Join(" | ", captures.GroupBy(c => c.Message ?? "-").Select(g => $"\"{g.Key}\" x{g.Count()}"))}");

        report.AppendLine();
        report.AppendLine("== Transcriptions ==");
        var distinct = transcriptions.GroupBy(c => c.Transcription!.Trim()).ToList();
        report.AppendLine($"{transcriptions.Count} total, {distinct.Count} distinct, {transcriptions.Sum(c => c.Transcription!.Length)} chars");
        foreach (var group in distinct.OrderBy(g => g.First().Number))
        {
            var text = group.Key.ReplaceLineEndings(" ");
            var numbers = string.Join(",", group.Select(c => c.Number));
            report.AppendLine($"  [{numbers}] ({text.Length} ch, {LetterRatio(text):P0} letters) {Preview(text)}");
        }

        report.AppendLine();
        report.AppendLine("== Attachments ==");
        report.AppendLine($"{attachments.Count} total, {attachments.Sum(c => c.Content!.LongLength):N0} decoded bytes");
        foreach (var capture in attachments)
        {
            var content = capture.Content!;
            var sniffed = FileSniffer.Sniff(content);
            var labelNote = capture.Meta is null ? "no meta" : SameType(capture.Meta, sniffed) ? "label matches" : $"LABEL MISMATCH (bytes say {sniffed})";
            var sizeNote = capture.DeclaredSize == content.LongLength ? "" : $", declared {capture.DeclaredSize}";
            report.AppendLine($"  #{capture.Number:D3} {capture.Meta ?? "?"}  {content.Length:N0} B{sizeNote}  {labelNote}");
            if (FileSniffer.LooksLikeText(content))
                report.AppendLine($"        {Preview(Encoding.UTF8.GetString(content).ReplaceLineEndings(" "))}");
        }

        if (empty.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("== Empty replies ==");
            foreach (var capture in empty)
                report.AppendLine($"  #{capture.Number:D3} code {capture.Code} \"{capture.Message}\"  {Path.GetFileName(capture.RawPath)}");
        }

        return report.ToString();
    }

    private static bool SameType(string meta, string sniffed) =>
        FileSniffer.Extension(meta) == FileSniffer.Extension(sniffed)
        || (sniffed == "text/plain" && meta.StartsWith("text/", StringComparison.OrdinalIgnoreCase));

    private static double LetterRatio(string text) => text.Length == 0 ? 0 : text.Count(char.IsLetter) / (double)text.Length;

    private static string Preview(string text) => text.Length <= PreviewLength ? text : text[..PreviewLength] + "…";
}
