using _05_02_zadanie.Hub;

namespace _05_02_zadanie.Call;

/// <summary>
/// One call on disk: every hub reply verbatim, every recording received, and a running transcript of both
/// sides, so a burned call can be read back turn by turn.
/// </summary>
public sealed class CallRecorder(string runDirectory)
{
    public string RunDirectory { get; } = runDirectory;

    public static CallRecorder CreateRun(string cacheDirectory)
    {
        var directory = Path.Combine(cacheDirectory, $"run-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(directory);
        return new CallRecorder(directory);
    }

    public void SaveReply(int turn, string label, HubReply reply) =>
        File.WriteAllText(Path.Combine(RunDirectory, $"turn-{turn:D2}-{label}.reply.json"), reply.Body);

    public string SaveOperatorAudio(int turn, int index, ReplyAudio audio)
    {
        var path = Path.Combine(RunDirectory, $"turn-{turn:D2}-operator{(index > 0 ? $"-{index}" : "")}.{audio.Extension}");
        File.WriteAllBytes(path, audio.Bytes);
        return path;
    }

    public void AppendTranscript(string speaker, string text) =>
        File.AppendAllText(Path.Combine(RunDirectory, "transcript.txt"), $"[{DateTime.Now:HH:mm:ss}] {speaker}: {text}{Environment.NewLine}");
}
