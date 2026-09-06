using System.Text.Json;
using _02_02_zadanie.Board;
using _02_02_zadanie.Hub;

namespace _02_02_zadanie.Tools;

/// <summary>
/// Downloads the current board PNG from the hub and returns a per-tile text description
/// produced by the vision model. Vision stays out of the main agent loop on purpose —
/// the agent reasons over text and never sees the image itself.
/// </summary>
public class ReadBoardTool : ITool
{
    private readonly HubClient _hub;
    private readonly TileVisionService _vision;
    private int _readCount;

    public ReadBoardTool(HubClient hub, TileVisionService vision)
    {
        _hub = hub;
        _vision = vision;
    }

    public string Name => "read_board";

    public string Description =>
        "Reads the CURRENT state of the puzzle board. Downloads a fresh board image and describes " +
        "every tile as the set of edges its cable exits through. The vision quota is a scarce " +
        "daily budget, so read once, plan a batch of rotations, then re-read to verify. " +
        "Set reset=true only to restore the board to its initial state.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "reset": {
              "type": "boolean",
              "description": "When true, resets the board to its initial state before reading. Default false."
            }
          },
          "required": []
        }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var reset = false;
        using (var args = JsonDocument.Parse(argumentsJson))
        {
            if (args.RootElement.TryGetProperty("reset", out var resetEl))
                reset = resetEl.ValueKind == JsonValueKind.True;
        }

        Console.WriteLine($"  [read_board] fetching board (reset={reset}), describing 9 tiles via vision...");
        var boardPng = await _hub.GetBoardPngAsync(reset, cancellationToken);
        var description = await _vision.DescribeBoardAsync(boardPng, $"current_{++_readCount}", cancellationToken);

        return "CURRENT BOARD STATE:\n" + description;
    }
}
