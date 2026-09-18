namespace _03_05_zadanie;

/// <summary>
/// Everything about the task that is not a provider setting, bound from the "SaveThem" section.
/// The world itself is deliberately absent: the map, the vehicle table and the terrain rules are
/// discovered at runtime through the tool search, so a stale briefing cannot outvote what the
/// archive actually says.
/// </summary>
public class TaskSettings
{
    public string HubBaseUrl { get; set; } = "https://hub.ag3nts.org";

    /// <summary>The only endpoint this run knows up front; every other tool is found through it.</summary>
    public string ToolSearchPath { get; set; } = "/api/toolsearch";

    public string CacheDirectory { get; set; } = "savethem-cache";

    public int MaxIterations { get; set; } = 40;

    /// <summary>How many calls the whole run may make to the hub's tool endpoints.</summary>
    public int MaxHubRequests { get; set; } = 60;

    /// <summary>
    /// Spacing between consecutive hub calls. The tool endpoints answer 429 after a burst and stay
    /// closed for about a minute, which costs far more than waiting a few seconds up front.
    /// </summary>
    public double MinSecondsBetweenHubRequests { get; set; } = 3.0;

    /// <summary>How many routes the run may send to /verify.</summary>
    public int MaxSubmissions { get; set; } = 5;

    /// <summary>
    /// Undocumented limit of the tool endpoints: a longer query comes back as -617 "Field query is
    /// too long". Measured, not guessed — the search endpoint itself does not enforce it.
    /// </summary>
    public int MaxToolQueryLength { get; set; } = 80;

    /// <summary>
    /// Words the run searches the registry for before the loop starts. They are the nouns of the
    /// briefing, not knowledge of what exists: the agent should enter the loop knowing which tools
    /// are out there instead of spending iterations discovering that the registry has any at all.
    /// </summary>
    public string[] BootstrapQueries { get; set; } = [];

    public IReadOnlyList<string> EffectiveBootstrapQueries =>
        BootstrapQueries.Length > 0 ? BootstrapQueries : ["terrain", "map", "vehicle", "notes", "route", "supplies"];
}
