namespace _05_02_zadanie.Call;

public enum RoadStatus
{
    Unknown,
    Passable,
    Blocked
}

public sealed record RoadReading(string Code, RoadStatus Status, string Quote);

/// <summary>
/// What the operator's last turn said, in the only shape the call needs. Everything here is a reading of
/// their words; what to do about it is decided by <see cref="TurnPlanner"/>, not by the model.
/// </summary>
public sealed record OperatorReading(
    IReadOnlyList<RoadReading> Roads,
    bool AsksForPassword,
    bool AsksWhy,
    string OtherQuestion,
    IReadOnlyList<string> MonitoringDisabled,
    bool CallBurned,
    string Summary)
{
    public static OperatorReading Silent { get; } = new([], false, false, "", [], false, "");
}
