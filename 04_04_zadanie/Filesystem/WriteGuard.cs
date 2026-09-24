using System.Text.Json;
using System.Text.RegularExpressions;

namespace _04_04_zadanie.Filesystem;

/// <summary>
/// The shape the task asks for, and the limits the API's own help states: names match
/// ^[a-z0-9_]+$, a file name has at most 20 characters and every name is unique across the whole tree.
/// </summary>
public static class FilesystemLayout
{
    public const string Cities = "/miasta";
    public const string People = "/osoby";
    public const string Goods = "/towary";

    public const int MaxFileNameLength = 20;

    public static readonly IReadOnlyList<string> Directories = [Cities, People, Goods];

    public static string TemplateFor(string directory) => directory switch
    {
        Cities => "miasto.md",
        People => "osoba.md",
        Goods => "towar.md",
        _ => throw new ArgumentException($"No template for '{directory}'.")
    };
}

public sealed record GuardVerdict(bool Allowed, string Path, string Reason)
{
    public static GuardVerdict Allow(string path, string reason = "") => new(true, path, reason);

    public static GuardVerdict Refuse(string path, string reason) => new(false, path, reason);
}

public sealed record MarkdownLink(string Label, string Target);

/// <summary>
/// Judges one write before it lands in the projection. These are the task's rules for the three
/// directories and the API's limits on names, applied in code: a file that would fail verification
/// costs the agent a tool turn here instead of a submission there. Grammar is the one thing left to
/// the model: the guard can tell "Ziemniaki" from "ziemniak" by its letters, not that one is plural.
/// </summary>
public static partial class WriteGuard
{
    [GeneratedRegex(@"\[([^\]\r\n]+)\]\(([^)\r\n]+)\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"^[a-z0-9_]+$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[a-z]+(?:_[a-z]+)*$")]
    private static partial Regex CityNamePattern();

    [GeneratedRegex(@"^[a-z]+(?:_[a-z]+)+$")]
    private static partial Regex PersonFileNamePattern();

    [GeneratedRegex(@"^[a-z]+$")]
    private static partial Regex GoodNamePattern();

    [GeneratedRegex(@"[A-Z][a-z]+ [A-Z][a-z]+(?: [A-Z][a-z]+)*")]
    private static partial Regex FullNamePattern();

    public static GuardVerdict Evaluate(VirtualFilesystem filesystem, string? path, string? content)
    {
        string normalized;
        try
        {
            normalized = VirtualFilesystem.NormalizePath(path);
        }
        catch (ArgumentException ex)
        {
            return GuardVerdict.Refuse(path ?? string.Empty, ex.Message);
        }

        var directory = VirtualFilesystem.Parent(normalized);
        var name = VirtualFilesystem.Name(normalized);

        if (!FilesystemLayout.Directories.Contains(directory))
            return GuardVerdict.Refuse(normalized, $"Files go directly into {string.Join(", ", FilesystemLayout.Directories)}; '{directory}' is not one of them and subdirectories are not part of the structure.");

        if (!PolishText.IsAscii(name))
            return GuardVerdict.Refuse(normalized, $"The file name contains non-ASCII characters ({PolishText.NonAsciiPreview(name)}). Names use plain letters: '{PolishText.Fold(name).ToLowerInvariant()}'.");

        if (name.Contains('.'))
            return GuardVerdict.Refuse(normalized, "The file name carries an extension. The file is named exactly as the thing it describes, without '.md' or '.json'.");

        if (!NamePattern().IsMatch(name))
            return GuardVerdict.Refuse(normalized, $"The file name must match the API's allowed_name_pattern ^[a-z0-9_]+$: lowercase ASCII letters, digits and '_' only. Try '{Suggest(name)}'.");

        if (name.Length > FilesystemLayout.MaxFileNameLength)
            return GuardVerdict.Refuse(normalized, $"The file name has {name.Length} characters; the API allows at most {FilesystemLayout.MaxFileNameLength}.");

        var sameName = filesystem.PathsNamed(name).Where(other => other != normalized).ToList();
        if (sameName.Count > 0)
            return GuardVerdict.Refuse(normalized, $"Names are unique across the whole filesystem and '{name}' already exists as {string.Join(", ", sameName)}.");

        if (content is null || content.Trim().Length == 0)
            return GuardVerdict.Refuse(normalized, "The content is empty.");

        if (!PolishText.IsAscii(content))
            return GuardVerdict.Refuse(normalized, $"The content contains non-ASCII characters ({PolishText.NonAsciiPreview(content)}). Write it without Polish letters.");

        return directory switch
        {
            FilesystemLayout.Cities => JudgeCity(normalized, name, content),
            FilesystemLayout.People => JudgePerson(filesystem, normalized, name, content),
            _ => JudgeGood(filesystem, normalized, name, content)
        };
    }

    public static IReadOnlyList<MarkdownLink> ExtractLinks(string content) =>
        LinkPattern().Matches(content).Select(match => new MarkdownLink(match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim())).ToList();

    public static string? ExtractFullName(string content) => FullNamePattern().Match(content) is { Success: true } match ? match.Value : null;

    private static string Suggest(string name) =>
        new(PolishText.Fold(name).ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_').ToArray());

    /// <summary>
    /// The one bit of grammar the guard does know: Polish plural nominatives of goods end in -i or -y
    /// (koparki, worki, lopaty) and singulars do not. The ledger itself writes some items in the plural,
    /// so an exact copy of the ledger is not proof of the singular.
    /// </summary>
    private static bool LooksPlural(string name) => name.Length > 2 && name[^1] is 'i' or 'y';

    private static string PluralReason(string what, string name) =>
        $"{what} '{name}' looks like a plural: it ends in -i/-y, the Polish plural ending (koparki -> koparka, worki -> worek, lopaty -> lopata). Write the singular nominative.";

    private static GuardVerdict JudgeCity(string path, string name, string content)
    {
        if (!CityNamePattern().IsMatch(name))
            return GuardVerdict.Refuse(path, "A city file is named by the city in the nominative: lowercase letters, an underscore between the words of a multi-word name, e.g. 'komarowo'.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            return GuardVerdict.Refuse(path, $"The content is not valid JSON ({ex.Message}). A city file holds one JSON object and nothing else, no markdown fences.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return GuardVerdict.Refuse(path, "The JSON must be an object mapping each needed good to its quantity.");

            var count = 0;
            foreach (var property in root.EnumerateObject())
            {
                count++;
                if (!GoodNamePattern().IsMatch(property.Name))
                    return GuardVerdict.Refuse(path, $"Key '{property.Name}': a good is one lowercase ASCII word in the singular nominative, e.g. 'koparka'.");

                if (LooksPlural(property.Name))
                    return GuardVerdict.Refuse(path, PluralReason("Key", property.Name));

                if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt64(out var quantity) || quantity <= 0)
                    return GuardVerdict.Refuse(path, $"Key '{property.Name}': the value must be a positive integer without units, e.g. 45, not '{property.Value.GetRawText()}'.");
            }

            return GuardVerdict.Allow(path, count == 0 ? "The city has no needs listed." : $"{count} needed goods.");
        }
    }

    private static GuardVerdict JudgePerson(VirtualFilesystem filesystem, string path, string name, string content)
    {
        if (!PersonFileNamePattern().IsMatch(name))
            return GuardVerdict.Refuse(path, "A person file is named 'firstname_surname': lowercase words joined by an underscore, e.g. 'jan_kowalski'. " + MissingHalfHint);

        var fullName = ExtractFullName(content);
        if (fullName is null)
            return GuardVerdict.Refuse(path, "The content must state the person's full name: first name and surname, both capitalised. " + MissingHalfHint);

        var words = fullName.Split(' ');
        if (words.Distinct(StringComparer.OrdinalIgnoreCase).Count() != words.Length)
            return GuardVerdict.Refuse(path, $"The name '{fullName}' repeats a word. A first name and a surname are different words. " + MissingHalfHint);

        var links = ExtractLinks(content);
        if (links.Count != 1)
            return GuardVerdict.Refuse(path, $"The content must carry exactly one markdown link to the city this person manages; found {links.Count}.");

        return CheckCityLink(filesystem, path, links[0]) ?? GuardVerdict.Allow(path, $"{fullName} manages {links[0].Target}.");
    }

    private const string MissingHalfHint =
        "If you know only half of the name, the diary gives the other half in another entry about the same city and the same matter; " +
        "join the two, never repeat or invent a word to satisfy the shape.";

    private static GuardVerdict JudgeGood(VirtualFilesystem filesystem, string path, string name, string content)
    {
        if (!GoodNamePattern().IsMatch(name))
            return GuardVerdict.Refuse(path, "A good file is named by the good in the singular nominative, lowercase ASCII letters only, e.g. 'koparka'.");

        if (LooksPlural(name))
            return GuardVerdict.Refuse(path, PluralReason("The file name", name));

        var links = ExtractLinks(content);
        if (links.Count == 0)
            return GuardVerdict.Refuse(path, "The content must link to every city that sells this good; found no markdown link.");

        foreach (var link in links)
        {
            if (CheckCityLink(filesystem, path, link) is { } refusal)
                return refusal;
        }

        var duplicates = links.GroupBy(link => VirtualFilesystem.NormalizePath(link.Target)).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        if (duplicates.Count > 0)
            return GuardVerdict.Refuse(path, $"The same city is linked more than once: {string.Join(", ", duplicates)}.");

        return GuardVerdict.Allow(path, $"sold by {links.Count} cities.");
    }

    private static GuardVerdict? CheckCityLink(VirtualFilesystem filesystem, string path, MarkdownLink link)
    {
        string target;
        try
        {
            target = VirtualFilesystem.NormalizePath(link.Target);
        }
        catch (ArgumentException)
        {
            return GuardVerdict.Refuse(path, $"Link target '{link.Target}' is not an absolute path. Link to a city as [Name]({FilesystemLayout.Cities}/name).");
        }

        if (VirtualFilesystem.Parent(target) != FilesystemLayout.Cities)
            return GuardVerdict.Refuse(path, $"Link target '{link.Target}' does not point into {FilesystemLayout.Cities}. Only city files are linked.");

        if (!filesystem.FileExists(target))
            return GuardVerdict.Refuse(path, $"Link target '{target}' does not exist yet. Create the city file first, then link to it.");

        return null;
    }
}
