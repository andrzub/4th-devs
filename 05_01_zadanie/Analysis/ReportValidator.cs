using System.Text.Json.Nodes;

namespace _05_01_zadanie.Analysis;

public sealed record RadioReport(string CityName, string CityArea, int WarehousesCount, string PhoneNumber)
{
    public JsonObject ToAnswer() => new()
    {
        ["action"] = "transmit",
        ["cityName"] = CityName,
        ["cityArea"] = CityArea,
        ["warehousesCount"] = WarehousesCount,
        ["phoneNumber"] = PhoneNumber
    };
}

public sealed record ValidationResult(RadioReport? Report, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0 && Report is not null;
}

/// <summary>
/// Turns the analyst's findings into the transmit payload, or refuses. The city must exist in the
/// records (that is where the area comes from), and both quotes must be found in the corpus.
/// </summary>
public static class ReportValidator
{
    public static ValidationResult Validate(AnalystFindings findings, EvidenceCorpus corpus, CityTable cities)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var city = cities.Find(findings.CityName);
        if (city is null)
            errors.Add($"City '{findings.CityName}' is not among the city records ({string.Join(", ", cities.Records.Select(r => r.Name))}); its area cannot be read.");

        if (findings.WarehousesCount <= 0)
            errors.Add($"warehousesCount is {findings.WarehousesCount}; the analyst did not find it.");
        if (!corpus.Contains(findings.WarehousesQuote))
            errors.Add($"The warehouses quote is not in the material: \"{findings.WarehousesQuote}\"");

        var phone = new string(findings.PhoneNumber.Where(char.IsDigit).ToArray());
        if (phone.Length == 0)
            errors.Add("phoneNumber is empty; the analyst did not find it.");
        else if (phone.Length != 9)
            warnings.Add($"phoneNumber has {phone.Length} digits, a Polish number has 9.");
        if (phone != findings.PhoneNumber.Trim())
            warnings.Add($"phoneNumber normalised from \"{findings.PhoneNumber}\" to \"{phone}\".");
        if (!corpus.Contains(findings.PhoneQuote))
            errors.Add($"The phone quote is not in the material: \"{findings.PhoneQuote}\"");
        else if (!new string(findings.PhoneQuote.Where(char.IsDigit).ToArray()).Contains(phone))
            warnings.Add("The phone quote does not contain the number as digits (spelled out in words?); check it by hand.");

        var report = city is null ? null : new RadioReport(city.Name, CityTable.FormatArea(city.Area), findings.WarehousesCount, phone);
        return new ValidationResult(report, errors, warnings);
    }
}
