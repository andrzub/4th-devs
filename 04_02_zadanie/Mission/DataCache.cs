using _04_02_zadanie.Analysis;

namespace _04_02_zadanie.Mission;

/// <summary>
/// What the run knows before the window opens. The documentation is served outside any session, so
/// it is simply fetched; the forecast and the deficit can only be read inside one, so they are kept
/// from the previous window and treated as an assumption to be re-checked, never as fact.
/// </summary>
public sealed class DataCache(string directory)
{
    public string DocumentationPath => Path.Combine(directory, "documentation.json");

    public string ForecastPath => Path.Combine(directory, "weather.json");

    public string PlantPath => Path.Combine(directory, "powerplant.json");

    public void Save(string path, string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, json);
    }

    public TurbineModel LoadTurbine() => TurbineModel.Parse(Read(DocumentationPath, "documentation", "--doc"));

    public WeatherForecast LoadForecast() => WeatherForecast.Parse(Read(ForecastPath, "weather forecast", "--recon"));

    public ValueRange? TryLoadDeficit()
    {
        try
        {
            return LoadDeficit();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public ValueRange LoadDeficit()
    {
        var plant = System.Text.Json.Nodes.JsonNode.Parse(Read(PlantPath, "plant report", "--recon"))?.AsObject();

        return ValueRange.TryParse(plant?["powerDeficitKw"]?.ToString(), out var deficit)
            ? deficit
            : throw new InvalidOperationException("The cached plant report carries no powerDeficitKw.");
    }

    /// <summary>
    /// Picks up the reports an earlier reconnaissance run left behind, so a fresh checkout does not
    /// have to spend a service window before it can plan anything.
    /// </summary>
    public void AdoptFromReconRuns()
    {
        Adopt(ForecastPath, "*getResult*weather*.json");
        Adopt(PlantPath, "*getResult*powerplantcheck*.json");
    }

    private void Adopt(string destination, string pattern)
    {
        if (File.Exists(destination) || !Directory.Exists(directory))
            return;

        var newest = Directory.EnumerateDirectories(directory)
            .SelectMany(folder => Directory.EnumerateFiles(folder, pattern))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (newest is not null)
            File.Copy(newest, destination);
    }

    private static string Read(string path, string what, string mode) =>
        File.Exists(path)
            ? File.ReadAllText(path)
            : throw new InvalidOperationException($"No cached {what} at {path}. Run {mode} first.");
}
