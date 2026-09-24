using System.Text;
using System.Text.RegularExpressions;
using _04_04_zadanie.Notes;

namespace _04_04_zadanie.Filesystem;

public enum Severity
{
    Error,
    Warning
}

public sealed record Finding(Severity Severity, string Message);

public sealed record ValidationReport(IReadOnlyList<Finding> Findings)
{
    public bool IsValid => Findings.All(finding => finding.Severity != Severity.Error);

    public int Errors => Findings.Count(finding => finding.Severity == Severity.Error);

    public string Render()
    {
        if (Findings.Count == 0)
            return "Valid: no findings.";

        var builder = new StringBuilder();
        foreach (var finding in Findings)
            builder.AppendLine($"{(finding.Severity == Severity.Error ? "ERROR" : "warn ")}  {finding.Message}");
        builder.Append(IsValid ? "Valid, with warnings." : $"Invalid: {Errors} error(s).");
        return builder.ToString();
    }
}

/// <summary>
/// What the notes say in a form code can compare against: the announcements (the only source of
/// quantities), the diary (the only source of names) and the ledger (the only source of sellers).
/// </summary>
public sealed partial record ValidationSources(string Announcements, string Diary, IReadOnlyList<Transaction> Transactions)
{
    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberPattern();

    public IReadOnlySet<long> AnnouncedNumbers { get; } =
        NumberPattern().Matches(Announcements).Select(match => long.Parse(match.Value)).ToHashSet();

    public string FoldedNotes { get; } = PolishText.Fold(Announcements + "\n" + Diary).ToLowerInvariant();

    /// <summary>Picks the three notes by their names as the archive's README describes them.</summary>
    public static ValidationSources FromNotes(NoteLibrary notes)
    {
        string Find(string prefix) => notes.Names.FirstOrDefault(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                      ?? throw new FileNotFoundException($"No note starting with '{prefix}' among: {string.Join(", ", notes.Names)}.");

        return new ValidationSources(
            notes.Read(Find("og")).Content,
            notes.Read(Find("rozmowy")).Content,
            TransactionParser.Parse(notes.Read(Find("transakcje")).Content));
    }
}

/// <summary>
/// Judges the whole projection before it is sent. The write guard sees one file at a time; this
/// sees the structure: that every city has its person, that /towary mirrors the ledger seller by
/// seller, and that no name or number was invented. The ledger's items are matched to /towary files
/// by exact name or by a shared stem, because the ledger writes "ziemniaki" where the file is
/// "ziemniak" and the code does not know Polish declension.
/// </summary>
public static class PlanValidator
{
    public static ValidationReport Validate(VirtualFilesystem filesystem, ValidationSources sources)
    {
        var findings = new List<Finding>();
        void Error(string message) => findings.Add(new Finding(Severity.Error, message));
        void Warn(string message) => findings.Add(new Finding(Severity.Warning, message));

        CheckLayout(filesystem, Error);
        CheckEveryFile(filesystem, Error, Warn);
        CheckPeople(filesystem, sources, Error);
        CheckCities(filesystem, sources, Error);
        CheckGoods(filesystem, sources, Error);

        return new ValidationReport(findings);
    }

    private static void CheckLayout(VirtualFilesystem filesystem, Action<string> error)
    {
        var root = filesystem.List("/");
        var expected = FilesystemLayout.Directories.Select(directory => VirtualFilesystem.Name(directory) + "/").ToList();

        foreach (var missing in expected.Except(root))
            error($"Directory {missing.TrimEnd('/')} is missing.");
        foreach (var extra in root.Except(expected))
            error($"'{extra}' does not belong at the root; only {string.Join(", ", FilesystemLayout.Directories)} do.");

        foreach (var directory in FilesystemLayout.Directories)
        {
            if (filesystem.DirectoryExists(directory) && filesystem.FilesIn(directory).Count == 0)
                error($"{directory} is empty.");
        }
    }

    private static void CheckEveryFile(VirtualFilesystem filesystem, Action<string> error, Action<string> warn)
    {
        // Judged again on the final state: a city deleted after a person linked to it leaves that
        // person pointing nowhere, and the per-write guard could not see it coming.
        foreach (var (path, content) in filesystem.Files)
        {
            var verdict = WriteGuard.Evaluate(filesystem, path, content);
            if (!verdict.Allowed)
                error($"{path}: {verdict.Reason}");
            else if (VirtualFilesystem.Parent(path) == FilesystemLayout.Cities && content.Trim() == "{}")
                warn($"{path}: no needs listed.");
        }
    }

    private static void CheckPeople(VirtualFilesystem filesystem, ValidationSources sources, Action<string> error)
    {
        if (!filesystem.DirectoryExists(FilesystemLayout.People) || !filesystem.DirectoryExists(FilesystemLayout.Cities))
            return;

        var managers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var city in filesystem.FilesIn(FilesystemLayout.Cities))
            managers[city] = [];

        foreach (var person in filesystem.FilesIn(FilesystemLayout.People))
        {
            var content = filesystem.ReadFile(person);
            var fullName = WriteGuard.ExtractFullName(content);
            if (fullName is not null)
            {
                foreach (var word in fullName.Split(' '))
                {
                    if (!AppearsInNotes(word, sources))
                        error($"{person}: '{word}' does not appear in the notes; names are copied from the diary, not invented.");
                }
            }

            foreach (var link in WriteGuard.ExtractLinks(content))
            {
                if (TryNormalize(link.Target) is { } target && managers.TryGetValue(target, out var list))
                    list.Add(VirtualFilesystem.Name(person));
            }
        }

        foreach (var (city, people) in managers)
        {
            if (people.Count == 0)
                error($"{city}: no person in {FilesystemLayout.People} links to this city.");
            else if (people.Count > 1)
                error($"{city}: {people.Count} people link to this city ({string.Join(", ", people)}); each city has one person responsible.");
        }
    }

    private static void CheckCities(VirtualFilesystem filesystem, ValidationSources sources, Action<string> error)
    {
        if (!filesystem.DirectoryExists(FilesystemLayout.Cities))
            return;

        foreach (var city in filesystem.FilesIn(FilesystemLayout.Cities))
        {
            var name = VirtualFilesystem.Name(city);
            if (!AppearsInNotes(name, sources))
                error($"{city}: '{name}' does not appear in the notes; city names are copied from the notes, not invented.");

            // Every quantity must exist somewhere on the announcements board. This catches invented
            // or mis-copied numbers, not misassigned ones: that part is the agent's reading.
            foreach (var quantity in Quantities(filesystem.ReadFile(city)))
            {
                if (!sources.AnnouncedNumbers.Contains(quantity))
                    error($"{city}: the quantity {quantity} appears nowhere on the announcements board.");
            }
        }
    }

    private static void CheckGoods(VirtualFilesystem filesystem, ValidationSources sources, Action<string> error)
    {
        if (!filesystem.DirectoryExists(FilesystemLayout.Goods) || !filesystem.DirectoryExists(FilesystemLayout.Cities))
            return;

        var cities = filesystem.FilesIn(FilesystemLayout.Cities).Select(VirtualFilesystem.Name).ToHashSet(StringComparer.Ordinal);
        var goods = filesystem.FilesIn(FilesystemLayout.Goods).ToDictionary(VirtualFilesystem.Name, path => path, StringComparer.Ordinal);
        var sellersByGood = goods.ToDictionary(
            pair => pair.Key,
            pair => WriteGuard.ExtractLinks(filesystem.ReadFile(pair.Value)).Select(link => TryNormalize(link.Target)).Where(target => target is not null).Select(target => VirtualFilesystem.Name(target!)).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        var expectedSellers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var unmatchedItems = new HashSet<string>(StringComparer.Ordinal);
        var missingCities = new HashSet<string>(StringComparer.Ordinal);

        foreach (var transaction in sources.Transactions)
        {
            var seller = PolishText.Fold(transaction.Seller).ToLowerInvariant();
            if (!cities.Contains(seller))
                missingCities.Add(transaction.Seller);
            if (!cities.Contains(PolishText.Fold(transaction.Buyer).ToLowerInvariant()))
                missingCities.Add(transaction.Buyer);

            var item = PolishText.Fold(transaction.Item).ToLowerInvariant();
            var matches = MatchGood(item, goods.Keys);
            if (matches.Count == 0)
            {
                unmatchedItems.Add(transaction.Item);
                continue;
            }

            if (matches.Count > 1)
            {
                error($"Ledger: item '{transaction.Item}' matches several files in {FilesystemLayout.Goods} ({string.Join(", ", matches)}); one good, one file.");
                continue;
            }

            if (!expectedSellers.TryGetValue(matches[0], out var sellers))
                expectedSellers[matches[0]] = sellers = new HashSet<string>(StringComparer.Ordinal);
            sellers.Add(seller);
        }

        foreach (var city in missingCities)
            error($"Ledger: '{city}' trades but has no file in {FilesystemLayout.Cities}.");

        foreach (var item in unmatchedItems)
            error($"Ledger: '{item}' is sold but has no file in {FilesystemLayout.Goods} (expected the singular nominative, ASCII).");

        foreach (var (good, path) in goods)
        {
            if (!expectedSellers.TryGetValue(good, out var expected))
            {
                error($"{path}: no transaction in the ledger sells '{good}'.");
                continue;
            }

            var actual = sellersByGood[good];
            foreach (var missing in expected.Except(actual))
                error($"{path}: {missing} sells this good in the ledger but is not linked.");
            foreach (var extra in actual.Except(expected))
                error($"{path}: links {extra}, which never sells this good in the ledger.");
        }
    }

    /// <summary>
    /// Files whose name is the ledger's item or shares its stem: equal length within two letters and
    /// a common prefix covering all but the last letter of the shorter. An exact match wins outright.
    /// </summary>
    internal static IReadOnlyList<string> MatchGood(string foldedItem, IEnumerable<string> goodNames)
    {
        var names = goodNames.ToList();
        if (names.Contains(foldedItem))
            return [foldedItem];

        return names.Where(name => SharesStem(foldedItem, name)).ToList();
    }

    internal static bool SharesStem(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2)
            return false;

        var common = 0;
        while (common < a.Length && common < b.Length && a[common] == b[common])
            common++;

        return common >= Math.Max(3, Math.Min(a.Length, b.Length) - 1);
    }

    /// <summary>
    /// A name is accepted when its stem (all but the last three letters, at least three) occurs in the
    /// notes, so an inflected mention ("z Pucka", "w Darzlubiu") still vouches for the base form.
    /// </summary>
    internal static bool AppearsInNotes(string name, ValidationSources sources)
    {
        var folded = PolishText.Fold(name).ToLowerInvariant();
        var stem = folded[..Math.Max(3, folded.Length - 3)];
        return folded.Length >= 3 && sources.FoldedNotes.Contains(stem, StringComparison.Ordinal);
    }

    private static IEnumerable<long> Quantities(string cityJson)
    {
        System.Text.Json.JsonDocument document;
        try
        {
            document = System.Text.Json.JsonDocument.Parse(cityJson);
        }
        catch (System.Text.Json.JsonException)
        {
            yield break;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                yield break;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.TryGetInt64(out var quantity))
                    yield return quantity;
            }
        }
    }

    private static string? TryNormalize(string path)
    {
        try
        {
            return VirtualFilesystem.NormalizePath(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
