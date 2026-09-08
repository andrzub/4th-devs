namespace _02_05_zadanie.Mission;

/// <summary>
/// Appends every message of the run to a text file, so the whole process (tool calls,
/// full tool results, feedback) can be reviewed after the console has scrolled away.
/// </summary>
public sealed class Transcript
{
    public Transcript(string filePath)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        File.WriteAllText(filePath, $"Run started {DateTimeOffset.Now:O}{Environment.NewLine}");
    }

    public string FilePath { get; }

    public void Append(string heading, string body) =>
        File.AppendAllText(FilePath, $"{Environment.NewLine}=== {heading} ==={Environment.NewLine}{body}{Environment.NewLine}");
}
