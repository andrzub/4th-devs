namespace _04_01_zadanie.Oko;

/// <summary>
/// One row of the operator console. Identifiers are shared across the pages: the same thirty-two
/// hex characters address a different record on "incydenty", on "notatki" and on "zadania", so a
/// record is only identified by the pair and never by the id alone.
/// </summary>
public sealed record OkoRecord(string Page, string Id, string Title, string Summary = "", string Meta = "", string Content = "", bool? Done = null)
{
    /// <summary>The six-character classification code the console keeps at the front of a title.</summary>
    public string? LeadingCode
    {
        get
        {
            var word = Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return word is { Length: 6 } && word[..4].All(char.IsAsciiLetterUpper) && word[4..].All(char.IsAsciiDigit) ? word : null;
        }
    }

    public string Describe()
    {
        var status = Done is null ? string.Empty : Done.Value ? " [done: YES]" : " [done: NO]";
        var meta = Meta.Length > 0 ? $" ({Meta})" : string.Empty;
        return $"{Page}/{Id}{status}{meta}{Environment.NewLine}  title: {Title}{Environment.NewLine}  {(Content.Length > 0 ? Content : Summary)}";
    }
}

public static class OkoPages
{
    public const string Incidents = "incydenty";
    public const string Notes = "notatki";
    public const string Tasks = "zadania";
    public const string Users = "uzytkownicy";

    public static readonly string[] All = [Incidents, Notes, Tasks, Users];

    /// <summary>Pages the okoeditor API accepts for an update; "uzytkownicy" is read-only.</summary>
    public static readonly string[] Editable = [Incidents, Notes, Tasks];

    public static bool Exists(string? page) => page is not null && All.Contains(page, StringComparer.OrdinalIgnoreCase);
}
