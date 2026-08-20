using _01_01_zadanie.Models;
using CsvHelper;
using System.Globalization;

namespace _01_01_zadanie.Services;

public sealed class PersonCsvReader
{
    public List<Person> Read(string path)
    {
        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        return csv.GetRecords<Person>().ToList();
    }
}
