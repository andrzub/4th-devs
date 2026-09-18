using System.Text;
using System.Text.Json;
using _03_05_zadanie.Hub;
using _03_05_zadanie.World;

namespace _03_05_zadanie.Mission;

/// <summary>
/// Everything the agent can do, in one place: find tools, ask them, write down the rules it found,
/// let the code plan the route and send one. The split is deliberate — discovery and interpretation
/// belong to the model, arithmetic over a grid and two budgets belongs here.
/// </summary>
public sealed class Expedition(HubClient hub, ToolRegistry registry, MissionState mission, TaskSettings settings, bool submissionEnabled)
{
    private readonly Dictionary<string, string> _answers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _rejections = new(StringComparer.OrdinalIgnoreCase);

    public WorldModel? World { get; private set; }

    public PlanningReport? LastPlan { get; private set; }

    public ToolRegistry Registry => registry;

    public MissionState Mission => mission;

    public bool SubmissionEnabled => submissionEnabled;

    /// <summary>A route that survived the guard while submissions were switched off.</summary>
    public bool RouteReady { get; private set; }

    /// <summary>
    /// What ends the run. With submissions on, only a flag from the hub counts; with them off, the
    /// deliverable is a route the guard accepts, so the loop has something to finish on.
    /// </summary>
    public bool IsSettled => submissionEnabled ? mission.FlagReceived : RouteReady;

    /// <summary>
    /// Searches the registry for the nouns of the briefing before the agent's first turn. Which
    /// tools exist is not something worth spending iterations on: a run that starts blind spends
    /// them inventing search terms, and every miss is a request the rate limit makes expensive.
    /// </summary>
    public async Task<string> BootstrapAsync(CancellationToken cancellationToken = default)
    {
        var digest = new StringBuilder();
        digest.AppendLine("The registry was already searched for the words of this briefing. Raw answers:");

        foreach (var query in settings.EffectiveBootstrapQueries)
        {
            var reply = await hub.AskToolAsync(settings.ToolSearchPath, query, cancellationToken);
            registry.Absorb(reply.Body);
            _answers[Key("search", query)] = reply.Body;
            digest.AppendLine($"  search \"{query}\" -> {reply.Body}");
        }

        digest.AppendLine();
        digest.AppendLine("Tools known so far:");
        digest.Append(registry.Render());
        return digest.ToString();
    }

    public async Task<string> SearchToolsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (_answers.TryGetValue(Key("search", query), out var remembered))
            return Annotate($"You already searched for this, and nothing has changed since. The answer was:{Environment.NewLine}{remembered}");

        var reply = await hub.AskToolAsync(settings.ToolSearchPath, query, cancellationToken);
        var learned = registry.Absorb(reply.Body);
        _answers[Key("search", query)] = reply.Body;

        var known = registry.Known.Count == 0 ? "none" : string.Join(", ", registry.Known.Select(tool => tool.Name));
        return Annotate($"{reply.Body}{Environment.NewLine}{learned} new tool(s) learned. Known tools: {known}.");
    }

    public async Task<string> AskToolAsync(string toolName, string query, CancellationToken cancellationToken = default)
    {
        if (!registry.TryResolve(toolName, out var tool))
            return $"No tool named {toolName} has been found yet. Known tools: {string.Join(", ", registry.Known.Select(known => known.Name))}.";

        // Refused here, the over-long query costs nothing; sent, it costs a request and comes back as -617.
        if (query.Length > settings.MaxToolQueryLength)
            return $"Not sent: {tool.Name} rejects a query longer than {settings.MaxToolQueryLength} characters and yours is {query.Length}. Send a value, not a sentence about it.";

        if (_answers.TryGetValue(Key(tool.Name, query), out var remembered))
            return Annotate($"You already sent this exact value to {tool.Name}. The answer was:{Environment.NewLine}{remembered}");

        var reply = await hub.AskToolAsync(tool.Url, query, cancellationToken);
        registry.Absorb(reply.Body);
        _answers[Key(tool.Name, query)] = reply.Body;

        return Annotate(reply.Body + RejectionHint(tool.Name, reply.Body));
    }

    /// <summary>
    /// Stores the rules the agent read out of the archive. The shape is checked, the content is not:
    /// a rule invented here shows up later as a route the simulator kills.
    /// </summary>
    public string RegisterWorld(JsonElement payload)
    {
        if (!WorldModel.TryParse(payload, out var world, out var error))
            return $"The world was not registered: {error}";

        World = world;
        LastPlan = null;
        return $"World registered.{Environment.NewLine}{world!.Render()}";
    }

    public string Plan()
    {
        if (World is null)
            return "Nothing to plan with yet: register the map, the consumption table and the budget first.";

        LastPlan = RoutePlanner.Plan(World);
        return LastPlan.Render();
    }

    public async Task<string> SubmitAsync(IReadOnlyList<string> instructions, CancellationToken cancellationToken = default)
    {
        if (World is null)
            return "Nothing to check the route against: register the world first.";

        var tokens = RouteInstructions.Split(instructions);
        var route = RouteInstructions.Render(tokens);
        var outcome = RouteSimulator.Run(World, tokens);

        if (!outcome.IsValid)
        {
            mission.Record(route, sent: false, $"refused before sending: {outcome.Failure}");
            return $"Not sent — the route does not work against the rules you registered.{Environment.NewLine}{outcome.Report}";
        }

        if (hub.SubmissionsSent >= settings.MaxSubmissions)
        {
            mission.Record(route, sent: false, "refused: submission budget spent");
            return $"Not sent: the run has already used its {settings.MaxSubmissions} submissions.";
        }

        if (!submissionEnabled)
        {
            RouteReady = true;
            mission.Record(route, sent: true, "dry run, nothing left this machine");
            return $"Dry run — nothing was sent. The route survives the rules you registered.{Environment.NewLine}{outcome.Report}";
        }

        var reply = await hub.SubmitAsync(tokens, cancellationToken);
        mission.ScanForFlag(reply.Body);
        mission.Record(route, sent: true, $"HTTP {reply.Status}: {reply.Body}");
        return Annotate($"HTTP {reply.Status}{Environment.NewLine}{reply.Body}");
    }

    public Task<string> ReadPreviewAsync(CancellationToken cancellationToken = default) =>
        hub.ReadPreviewAsync(cancellationToken).ContinueWith(task => PreviewReport.Render(task.Result.Body), cancellationToken);

    /// <summary>
    /// Counts the values a tool has turned down. A tool that keeps refusing is not being asked
    /// badly, it is being asked for the wrong kind of thing, and the count says so out loud.
    /// </summary>
    private string RejectionHint(string toolName, string body)
    {
        if (!IsRejection(body))
            return string.Empty;

        _rejections[toolName] = _rejections.GetValueOrDefault(toolName) + 1;
        var count = _rejections[toolName];
        return count < 2
            ? string.Empty
            : $"{Environment.NewLine}[{toolName} has now turned down {count} of your values. Its own message names the kind of value it wants — send one of those, not another wording of the same request.]";
    }

    private static bool IsRejection(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.GetDouble() < 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Key(string tool, string query) => $"{tool}|{query.Trim()}";

    private string Annotate(string body) =>
        $"{body}{Environment.NewLine}[budget: {hub.RemainingRequests} hub calls left, {settings.MaxSubmissions - hub.SubmissionsSent} submissions left]";
}
