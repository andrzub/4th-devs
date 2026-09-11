using System.Text;
using System.Text.RegularExpressions;

namespace _03_02_zadanie.Guard;

/// <summary>
/// One line of a .gitignore, compiled to a matcher over paths relative to the directory that
/// declared it. The supported subset is the one these files actually use: negation, anchoring,
/// directory-only rules, and the <c>*</c> / <c>?</c> / <c>**</c> wildcards. Anything the subset
/// cannot express is reported as unsupported so the guard can refuse the whole directory instead
/// of quietly under-matching.
/// </summary>
public sealed class GitignorePattern
{
    private readonly Regex _regex;

    private GitignorePattern(string source, bool isNegated, Regex regex)
    {
        Source = source;
        IsNegated = isNegated;
        _regex = regex;
    }

    public string Source { get; }
    public bool IsNegated { get; }

    public static GitignorePattern? TryParse(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            return null;

        var isNegated = trimmed.StartsWith('!');
        if (isNegated)
            trimmed = trimmed[1..].Trim();

        // A trailing slash marks a directory-only rule. It is dropped and the rule left to match
        // either kind of entry, which errs towards blocking rather than towards letting something through.
        var body = trimmed.TrimEnd('/');
        if (body.Length == 0)
            return null;

        // A pattern with a slash anywhere but at the end is anchored to the .gitignore's directory;
        // a bare name matches at any depth below it.
        var isAnchored = body.Contains('/');
        if (body.StartsWith('/'))
            body = body[1..];

        var regex = new Regex(BuildExpression(body, isAnchored), RegexOptions.CultureInvariant);
        return new GitignorePattern(trimmed, isNegated, regex);
    }

    /// <summary>
    /// Matches the entry itself and, because ignoring an entry ignores everything under it,
    /// any path that continues below it.
    /// </summary>
    public bool Matches(string relativePath) => _regex.IsMatch(relativePath);

    private static string BuildExpression(string body, bool isAnchored)
    {
        var pattern = new StringBuilder("^");
        if (!isAnchored)
            pattern.Append("(?:.*/)?");

        pattern.Append(Translate(body));

        // Either the entry itself or anything nested inside it.
        pattern.Append("(?:/.*)?$");
        return pattern.ToString();
    }

    private static string Translate(string body)
    {
        var pattern = new StringBuilder();

        for (var i = 0; i < body.Length; i++)
        {
            var character = body[i];

            if (character == '*')
            {
                var isDoubleStar = i + 1 < body.Length && body[i + 1] == '*';
                if (isDoubleStar)
                {
                    i++;
                    // '**/' collapses to "any number of directories", '**' alone to "anything".
                    if (i + 1 < body.Length && body[i + 1] == '/')
                    {
                        i++;
                        pattern.Append("(?:.*/)?");
                    }
                    else
                    {
                        pattern.Append(".*");
                    }
                }
                else
                {
                    pattern.Append("[^/]*");
                }

                continue;
            }

            pattern.Append(character switch
            {
                '?' => "[^/]",
                '/' => "/",
                _ => Regex.Escape(character.ToString())
            });
        }

        return pattern.ToString();
    }
}
