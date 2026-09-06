using System.Text.Json;
using System.Text.RegularExpressions;
using _02_02_zadanie.Hub;
using _02_02_zadanie.Mission;

namespace _02_02_zadanie.Tools;

/// <summary>
/// Submits one 90° clockwise rotation to the hub. Every call is a real /verify request,
/// so the tile address is validated in code before anything leaves the process.
/// The hub response is returned raw; the flag is detected here, never trusted from the model.
/// </summary>
public partial class RotateTileTool : ITool
{
    [GeneratedRegex("^[1-3]x[1-3]$")]
    private static partial Regex TileAddressRegex();

    private readonly HubClient _hub;
    private readonly MissionState _state;

    public RotateTileTool(HubClient hub, MissionState state)
    {
        _hub = hub;
        _state = state;
    }

    public string Name => "rotate_tile";

    public string Description =>
        "Rotates one tile of the board 90 degrees CLOCKWISE. A clockwise rotation moves each " +
        "cable exit to the next edge: U->R, R->D, D->L, L->U. To rotate counter-clockwise once, " +
        "rotate clockwise three times. Each call costs one API request, so plan rotations first.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "tile": {
              "type": "string",
              "pattern": "^[1-3]x[1-3]$",
              "description": "Tile address as RxC, e.g. '2x3' = row 2, column 3."
            }
          },
          "required": ["tile"]
        }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        string? tile;
        using (var args = JsonDocument.Parse(argumentsJson))
        {
            tile = args.RootElement.TryGetProperty("tile", out var tileEl) ? tileEl.GetString() : null;
        }

        if (tile is null || !TileAddressRegex().IsMatch(tile))
            return $"Invalid tile address '{tile}'. Use RxC with row and column in 1-3, e.g. '2x3'.";

        Console.WriteLine($"  [rotate_tile] rotating {tile} clockwise...");
        var response = await _hub.RotateTileAsync(tile, cancellationToken);

        _state.RecordRotation();
        _state.ScanForFlag(response);

        return response;
    }
}
