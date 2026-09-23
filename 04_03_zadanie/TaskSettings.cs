using _04_03_zadanie.City;
using _04_03_zadanie.Mission;

namespace _04_03_zadanie;

/// <summary>
/// Everything about the task that is not a provider setting, bound from the "Domatowo" section.
/// The map and the prices are absent on purpose: the map is read from the hub and the prices from
/// actionCost, so what the code hard-codes is only what help and the task text state as fixed rules.
/// </summary>
public class TaskSettings
{
    private static readonly string[] DefaultSpawnSlots = ["A6", "B6", "C6", "D6"];

    public string HubBaseUrl { get; set; } = "https://hub.ag3nts.org";

    public string CacheDirectory { get; set; } = "domatowo-cache";

    public int RequestTimeoutSeconds { get; set; } = 60;

    /// <summary>The hub's rate limits are undocumented and there is no clock in this task, so a short pause between calls is free insurance.</summary>
    public double MinSecondsBetweenRequests { get; set; } = 0.5;

    /// <summary>Action points for the whole operation.</summary>
    public int Budget { get; set; } = 300;

    public int MaxTransporters { get; set; } = 4;

    public int MaxScouts { get; set; } = 8;

    /// <summary>
    /// The operator's reading of the intercepted signal: "one of the tallest blocks" is the three-storey
    /// block. Changing the reading changes what gets searched, not the code.
    /// </summary>
    public string TargetSymbol { get; set; } = "B3";

    /// <summary>Where created units appear, from help ("A6 -> D6"). Empty here because the binder appends JSON entries to a non-empty default.</summary>
    public List<string> SpawnSlots { get; set; } = [];

    public IReadOnlyList<Coordinate> EffectiveSpawnSlots =>
        (SpawnSlots.Count > 0 ? SpawnSlots.AsEnumerable() : DefaultSpawnSlots).Select(Coordinate.Parse).ToList();

    public OperationState NewOperationState() =>
        new(EffectiveSpawnSlots) { Budget = Budget, MaxTransporters = MaxTransporters, MaxScouts = MaxScouts };
}
