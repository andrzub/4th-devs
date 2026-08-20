namespace _01_01_zadanie.Models;

public sealed record TaggedPerson(
    string Name,
    string Surname,
    string Gender,
    int Born,
    string City,
    IReadOnlyList<string> Tags);
