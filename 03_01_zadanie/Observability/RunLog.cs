using System.Text.Json;
using System.Text.Json.Nodes;

namespace _03_01_zadanie.Observability;

/// <summary>
/// The local stand-in for a platform like Langfuse, built on the same hierarchy: the run is a
/// session, a named trace is one logical pass over the data, and each model call is a
/// generation carrying its own tokens, cost and duration. Everything lands in one JSONL file
/// plus a per-run directory holding the exact prompts and raw replies, so an interaction can
/// be replayed and inspected after the console has scrolled away.
/// </summary>
public sealed class RunLog
{
    private readonly string _jsonlPath;
    private readonly object _lock = new();

    public RunLog(string cacheDirectory, string jsonlPath)
    {
        RunId = $"run-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
        RunDirectory = Path.Combine(cacheDirectory, RunId);
        Directory.CreateDirectory(RunDirectory);

        _jsonlPath = jsonlPath;
    }

    public string RunId { get; }
    public string RunDirectory { get; }

    public void Event(string trace, string name, JsonObject? data = null)
    {
        var entry = data ?? new JsonObject();
        entry["kind"] = "event";
        entry["trace"] = trace;
        entry["name"] = name;
        Write(entry);
    }

    public void Generation(string trace, string model, int items, int promptTokens, int cachedPromptTokens, int completionTokens, decimal cost, TimeSpan duration, string? note = null)
    {
        var entry = new JsonObject
        {
            ["kind"] = "generation",
            ["trace"] = trace,
            ["model"] = model,
            ["items"] = items,
            ["promptTokens"] = promptTokens,
            ["cachedPromptTokens"] = cachedPromptTokens,
            ["completionTokens"] = completionTokens,
            ["costUsd"] = cost,
            ["durationMs"] = (long)duration.TotalMilliseconds
        };

        if (note is not null)
            entry["note"] = note;

        Write(entry);
    }

    /// <summary>Stores the full text of one interaction so it can be re-read or replayed later.</summary>
    public void SaveArtifact(string fileName, string content)
    {
        var path = Path.Combine(RunDirectory, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void Write(JsonObject entry)
    {
        entry["runId"] = RunId;
        entry["timestamp"] = DateTimeOffset.Now.ToString("O");

        lock (_lock)
            File.AppendAllText(_jsonlPath, entry.ToJsonString(new JsonSerializerOptions()) + Environment.NewLine);
    }
}
