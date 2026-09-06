using System.Text.Json;
using _02_02_zadanie.Board;
using _02_02_zadanie.Hub;

namespace _02_02_zadanie.Tools;

/// <summary>
/// Describes the target (solved) board schema. The schema image is static, so both the
/// image and its vision description are cached on disk — repeated calls are free.
/// </summary>
public class ReadTargetTool : ITool
{
    private readonly HubClient _hub;
    private readonly TileVisionService _vision;
    private readonly string _targetImageUrl;
    private readonly string _descriptionCachePath;

    public ReadTargetTool(HubClient hub, TileVisionService vision, string targetImageUrl, string cacheDirectory)
    {
        _hub = hub;
        _vision = vision;
        _targetImageUrl = targetImageUrl;
        _descriptionCachePath = Path.Combine(cacheDirectory, "target-description.txt");
    }

    public string Name => "read_target";

    public string Description =>
        "Reads the TARGET board configuration (the solved schema). Returns the same per-tile " +
        "edge description format as read_board. The target never changes.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        { "type": "object", "properties": {}, "required": [] }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        if (File.Exists(_descriptionCachePath))
            return "TARGET BOARD STATE (cached):\n" + await File.ReadAllTextAsync(_descriptionCachePath, cancellationToken);

        Console.WriteLine("  [read_target] fetching target schema, describing 9 tiles via vision...");
        var targetPng = await _hub.GetImageAsync(_targetImageUrl, cancellationToken);
        var description = await _vision.DescribeBoardAsync(targetPng, "target", cancellationToken);

        await File.WriteAllTextAsync(_descriptionCachePath, description, cancellationToken);
        return "TARGET BOARD STATE:\n" + description;
    }
}
