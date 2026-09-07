using System.Globalization;
using System.Text.RegularExpressions;

namespace _02_04_zadanie.Mission;

/// <summary>
/// Deterministic format checks run before every submission. The task states both formats
/// exactly, so a malformed value is a code-level mistake to catch locally rather than a
/// question to ask the hub.
/// </summary>
public static partial class AnswerValidator
{
    public const int ConfirmationCodeLength = 36;
    private const string ConfirmationCodePrefix = "SEC-";

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex IsoDateRegex();

    public static IReadOnlyList<string> Validate(string? date, string? password, string? confirmationCode)
    {
        var problems = new List<string>();
        problems.AddRange(ValidateDate(date));
        problems.AddRange(ValidatePassword(password));
        problems.AddRange(ValidateConfirmationCode(confirmationCode));
        return problems;
    }

    public static IReadOnlyList<string> ValidateFact(MissionFact fact, string? value) => fact switch
    {
        MissionFact.Date => ValidateDate(value),
        MissionFact.Password => ValidatePassword(value),
        MissionFact.ConfirmationCode => ValidateConfirmationCode(value),
        _ => []
    };

    private static IReadOnlyList<string> ValidateDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
            return ["date is missing"];

        if (!IsoDateRegex().IsMatch(date))
            return [$"date \"{date}\" is not in YYYY-MM-DD format"];

        return DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? []
            : [$"date \"{date}\" is not a real calendar date"];
    }

    private static IReadOnlyList<string> ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return ["password is missing"];

        var problems = new List<string>();
        if (password != password.Trim())
            problems.Add("password has leading or trailing whitespace");
        if (password.Contains('\n') || password.Contains('\r'))
            problems.Add("password contains a line break, so more than the password itself was copied");
        return problems;
    }

    private static IReadOnlyList<string> ValidateConfirmationCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return ["confirmation_code is missing"];

        var problems = new List<string>();
        if (!code.StartsWith(ConfirmationCodePrefix, StringComparison.Ordinal))
            problems.Add($"confirmation_code \"{code}\" does not start with \"{ConfirmationCodePrefix}\"");

        if (code.Length != ConfirmationCodeLength)
            problems.Add($"confirmation_code has {code.Length} characters, expected {ConfirmationCodeLength} (\"{ConfirmationCodePrefix}\" plus 32)");

        if (code.Any(char.IsWhiteSpace))
            problems.Add("confirmation_code contains whitespace");

        return problems;
    }
}
