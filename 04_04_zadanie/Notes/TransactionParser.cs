namespace _04_04_zadanie.Notes;

public sealed record Transaction(string Seller, string Item, string Buyer);

/// <summary>
/// The transaction ledger is the one note with a regular shape, "seller -> item -> buyer" per line,
/// so it is read in code and used to check what the agent filed under /towary.
/// </summary>
public static class TransactionParser
{
    public static IReadOnlyList<Transaction> Parse(string text)
    {
        var transactions = new List<Transaction>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var parts = line.Split("->", StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || parts.Any(part => part.Length == 0))
                throw new FormatException($"Not a 'seller -> item -> buyer' line: '{line}'.");

            transactions.Add(new Transaction(parts[0], parts[1], parts[2]));
        }

        return transactions;
    }
}
