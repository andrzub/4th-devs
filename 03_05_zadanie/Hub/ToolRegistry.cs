using System.Text;
using System.Text.Json;

namespace _03_05_zadanie.Hub;

public sealed record DiscoveredTool(string Name, string Url, string Description);

/// <summary>
/// What the run has learned about the hub's own toolbox. Nothing is seeded: the only endpoint known
/// before the first call is the search itself, and every other one has to appear in a search result
/// before it can be called. That keeps invented endpoints from burning requests the rate limit
/// makes expensive.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, DiscoveredTool> _byName = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<DiscoveredTool> Known => _byName.Values;

    /// <summary>Picks the tool descriptors out of a raw search answer and returns how many were new.</summary>
    public int Absorb(string searchResponseBody)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(searchResponseBody);
        }
        catch (JsonException)
        {
            return 0;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array)
                return 0;

            var learned = 0;
            foreach (var tool in tools.EnumerateArray())
            {
                var name = Read(tool, "name");
                var url = Read(tool, "url");
                if (name.Length == 0 || url.Length == 0 || _byName.ContainsKey(name))
                    continue;

                _byName[name] = new DiscoveredTool(name, url, Read(tool, "description"));
                learned++;
            }

            return learned;
        }
    }

    public bool TryResolve(string nameOrUrl, out DiscoveredTool tool)
    {
        var wanted = nameOrUrl.Trim();
        if (_byName.TryGetValue(wanted, out tool!))
            return true;

        tool = _byName.Values.FirstOrDefault(candidate => string.Equals(candidate.Url, wanted, StringComparison.OrdinalIgnoreCase))!;
        return tool is not null;
    }

    public string Render()
    {
        if (_byName.Count == 0)
            return "No tools discovered yet.";

        var sb = new StringBuilder();
        foreach (var tool in _byName.Values.OrderBy(tool => tool.Name))
            sb.AppendLine($"{tool.Name,-12} {tool.Url,-20} {tool.Description}");

        return sb.ToString().TrimEnd();
    }

    private static string Read(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
}
