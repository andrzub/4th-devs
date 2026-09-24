using System.Text.Json.Nodes;

namespace _04_04_zadanie.Filesystem;

/// <summary>
/// The action names the hub's API expects. Defaults follow the task description; the rest is
/// confirmed against the API's own help before anything is sent.
/// </summary>
public sealed record ApiVocabulary(string CreateDirectory, string CreateFile, string Reset, string Done)
{
    public static readonly ApiVocabulary Default = new("createDirectory", "createFile", "reset", "done");
}

/// <summary>
/// Turns the projection into the batch the task documents: one array of actions, directories
/// before the files inside them. Reset and done are sent as separate requests so each gets its
/// own verdict from the hub.
/// </summary>
public static class BatchBuilder
{
    public static JsonArray Build(VirtualFilesystem filesystem, ApiVocabulary api)
    {
        var batch = new JsonArray();

        foreach (var directory in filesystem.Directories)
            batch.Add(new JsonObject { ["action"] = api.CreateDirectory, ["path"] = directory });

        foreach (var (path, content) in filesystem.Files)
            batch.Add(new JsonObject { ["action"] = api.CreateFile, ["path"] = path, ["content"] = content });

        return batch;
    }
}
