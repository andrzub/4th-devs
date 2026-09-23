using System.Text.Encodings.Web;
using System.Text.Json;

namespace _04_03_zadanie.Hub;

/// <summary>Keeps every raw reply on disk, pretty-printed, so a run can be read back after the fact.</summary>
public sealed class ReplyArchive(string root)
{
    private static readonly JsonSerializerOptions PrettyOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public string Root => root;

    public static string Format(HubReply reply) => reply.TryParseJson()?.ToJsonString(PrettyOptions) ?? reply.Body;

    public string Save(string fileName, HubReply reply)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, fileName);
        File.WriteAllText(path, Format(reply));
        return path;
    }

    public string SaveTimestamped(string prefix, HubReply reply) =>
        Save($"{prefix}-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.json", reply);
}
