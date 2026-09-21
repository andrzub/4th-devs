using System.Globalization;

namespace _04_02_zadanie.Analysis;

/// <summary>
/// The API reports yields and deficits as ranges ("4-5", "30-40", "100"), never as single numbers.
/// Keeping both ends apart all the way to the decision is the point: the schedule has to hold for
/// the worst yield against the largest deficit, not for a comfortable average of the two.
/// </summary>
public readonly record struct ValueRange(double Min, double Max)
{
    public static ValueRange Parse(string text)
    {
        var parts = text.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            1 => new ValueRange(Number(parts[0]), Number(parts[0])),
            2 => new ValueRange(Number(parts[0]), Number(parts[1])),
            _ => throw new FormatException($"Cannot read '{text}' as a number or a range.")
        };
    }

    public static bool TryParse(string? text, out ValueRange range)
    {
        try
        {
            range = Parse(text ?? string.Empty);
            return true;
        }
        catch (FormatException)
        {
            range = default;
            return false;
        }
    }

    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    public override string ToString() => Min.Equals(Max) ? Min.ToString("0.##", CultureInfo.InvariantCulture) : $"{Min:0.##}-{Max:0.##}";
}
