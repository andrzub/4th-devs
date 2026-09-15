namespace _03_03_zadanie;

/// <summary>
/// Everything about the task that is not a provider setting, bound from the "Reactor" section.
/// The board's own geometry is deliberately absent: dimensions and the goal column are read off
/// the board the API returns, so a briefing that turns out to be wrong cannot mislead the guard.
/// </summary>
public class TaskSettings
{
    public string HubBaseUrl { get; set; } = "https://hub.ag3nts.org";

    public string CacheDirectory { get; set; } = "reactor-cache";

    public int MaxIterations { get; set; } = 80;

    /// <summary>How many commands the whole run may send to the reactor.</summary>
    public int MaxCommands { get; set; } = 120;

    /// <summary>How many times a lost position may be thrown away and the crossing restarted.</summary>
    public int MaxResets { get; set; } = 2;
}
