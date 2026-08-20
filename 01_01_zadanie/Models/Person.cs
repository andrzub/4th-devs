namespace _01_01_zadanie.Models;

public sealed record Person(
    string Name,
    string Surname,
    string Gender,
    DateOnly BirthDate,
    string BirthPlace,
    string BirthCountry,
    string Job);
