using _01_01_zadanie.Models;

namespace _01_01_zadanie.Services;

public sealed class TransportCandidateSelector
{
    public List<Person> Select(IReadOnlyCollection<Person> people, int currentYear)
    {
        var selectedPeople = people
            .Where(person =>
                currentYear - person.BirthDate.Year >= 20
                && currentYear - person.BirthDate.Year <= 40
                && person is { BirthPlace: "Grudzi¹dz", BirthCountry: "Polska", Gender: "M" })
            .ToList();

        var duplicates = selectedPeople
            .GroupBy(person => (person.Name, person.Surname, person.BirthDate))
            .Where(group => group.Count() > 1)
            .ToList();

        if (duplicates.Count != 0)
        {
            var details = string.Join(", ", duplicates.Select(group => $"{group.Key.Name} {group.Key.Surname} ({group.Key.BirthDate:yyyy-MM-dd})"));
            throw new InvalidOperationException($"Duplicate people found in range: {details}");
        }

        return selectedPeople;
    }
}
