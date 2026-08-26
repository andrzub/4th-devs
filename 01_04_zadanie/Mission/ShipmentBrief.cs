namespace _01_04_zadanie.Mission;

/// <summary>
/// The shipment the declaration has to cover. Values are kept in Polish because they go
/// verbatim into a Polish document — translating them would corrupt the declaration.
/// </summary>
public static class ShipmentBrief
{
    public const string SenderId = "450202122";
    public const string Origin = "Gdańsk";
    public const string Destination = "Żarnowiec";
    public const int MassKg = 2800;
    public const string Contents = "kasety z paliwem do reaktora";
    public const int BudgetPp = 0;

    public static string Describe(DateOnly date) => $"""
        - Data nadania: {date:yyyy-MM-dd}
        - Nadawca (identyfikator): {SenderId}
        - Punkt nadawczy: {Origin}
        - Punkt docelowy: {Destination}
        - Waga: {MassKg} kg
        - Zawartość: {Contents}
        - Budżet: {BudgetPp} PP
        - Uwagi specjalne: brak
        """;
}
