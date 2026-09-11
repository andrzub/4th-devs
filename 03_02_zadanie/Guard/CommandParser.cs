using System.Text;

namespace _03_02_zadanie.Guard;

public sealed record ParseResult(ShellCommand? Command, string? Error)
{
    public static ParseResult Fail(string error) => new(null, error);
    public static ParseResult Ok(ShellCommand command) => new(command, null);
}

/// <summary>
/// Splits a raw command line into a verb and typed arguments. The machine runs a fixed command
/// dispatcher rather than a shell, so the grammar is small: whitespace separates tokens, quotes
/// group them, and the trailing free-text argument of <c>editline</c> is taken from the raw line
/// so that quoting cannot mangle the value being written.
/// </summary>
public static class CommandParser
{
    public static ParseResult Parse(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0)
            return ParseResult.Fail("Empty command.");

        if (line.Contains('\n') || line.Contains('\r'))
            return ParseResult.Fail("One command per call: the line contains a line break.");

        var tokens = Tokenize(line);
        if (tokens.Count == 0)
            return ParseResult.Fail("Empty command.");

        var verb = tokens[0];

        if (verb.StartsWith('/'))
        {
            // Running a program is done by naming it: the dispatcher has no exec verb.
            var passedArguments = tokens.Skip(1).Select(t => new CommandArgument(t, ArgumentKind.FreeText));
            return ParseResult.Ok(new ShellCommand(line, verb, [new CommandArgument(verb, ArgumentKind.Path), .. passedArguments]));
        }

        var spec = CommandSpec.Find(verb);
        if (spec is null)
            return ParseResult.Fail($"'{verb}' is not a command of this machine. Available: {CommandSpec.VerbList}. To run a program, give its absolute path as the whole command.");

        var arguments = spec.Verb == "editline" ? SplitEditline(line) : tokens.Skip(1).ToList();

        if (arguments.Count < spec.MinArguments || arguments.Count > spec.MaxArguments)
            return ParseResult.Fail($"{Describe(spec)} Usage: {spec.Usage}.");

        if (spec.Verb == "editline" && (!int.TryParse(arguments[1], out var lineNumber) || lineNumber < 1))
            return ParseResult.Fail($"editline needs a line number of 1 or more as its second argument, got '{arguments[1]}'. Usage: {spec.Usage}.");

        var typed = arguments.Select((value, index) => new CommandArgument(value, spec.KindAt(index))).ToList();
        return ParseResult.Ok(new ShellCommand(line, verb, typed));
    }

    private static string Describe(CommandSpec spec) => spec.MinArguments == spec.MaxArguments
        ? $"'{spec.Verb}' takes exactly {spec.MinArguments} argument(s)."
        : $"'{spec.Verb}' takes between {spec.MinArguments} and {spec.MaxArguments} arguments.";

    /// <summary>
    /// editline is a file, a line number and then everything else verbatim, spaces and quotes included.
    /// </summary>
    private static List<string> SplitEditline(string line)
    {
        var rest = line["editline".Length..].TrimStart();
        var parts = new List<string>();

        for (var field = 0; field < 2; field++)
        {
            var space = rest.IndexOf(' ');
            if (space < 0)
            {
                if (rest.Length > 0)
                    parts.Add(Unquote(rest));
                return parts;
            }

            parts.Add(Unquote(rest[..space]));
            rest = rest[(space + 1)..].TrimStart();
        }

        parts.Add(rest);
        return parts;
    }

    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';

        foreach (var character in line)
        {
            if (quote != '\0')
            {
                if (character == quote)
                    quote = '\0';
                else
                    current.Append(character);
            }
            else if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(character);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private static string Unquote(string token) =>
        token.Length >= 2 && token[0] is '"' or '\'' && token[^1] == token[0] ? token[1..^1] : token;
}
