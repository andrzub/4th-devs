using System.Text.RegularExpressions;
using _03_04_zadanie;
using _03_04_zadanie.Api;
using _03_04_zadanie.Catalog;
using _03_04_zadanie.Tests;
using _03_04_zadanie.Tools;
using Microsoft.Extensions.Configuration;

var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");

if (args.Contains("--tests"))
{
    return OfflineTests.Run(dataDirectory) ? 0 : 1;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection("Negotiations").Get<TaskSettings>() ?? new TaskSettings();
var log = new RequestLog(Path.Combine(AppContext.BaseDirectory, "negotiations-log.jsonl"));

var catalog = ItemCatalog.Load(dataDirectory);
var finder = new OfferFinder(new ItemMatcher(catalog));
var offersTool = new OffersTool(finder);
var citiesWithAllTool = new CitiesWithAllTool(finder);

var queryIndex = Array.IndexOf(args, "--query");
if (queryIndex >= 0 && queryIndex + 1 < args.Length)
{
    PrintToolAnswer(offersTool.Describe(args[queryIndex + 1]));
    return 0;
}

var commonIndex = Array.IndexOf(args, "--common");
if (commonIndex >= 0 && commonIndex + 1 < args.Length)
{
    PrintToolAnswer(citiesWithAllTool.Describe(args[commonIndex + 1]));
    return 0;
}

if (args.Contains("--submission") || args.Contains("--submit"))
{
    var baseUrl = ReadBaseUrl(args);
    if (baseUrl is null)
    {
        Console.Error.WriteLine("Brakuje publicznego adresu: --base-url https://twoj-tunel.example");
        return 1;
    }

    var tooLong = ToolCatalog.Validate(baseUrl);
    foreach (var problem in tooLong)
    {
        Console.Error.WriteLine($"Opis nie miesci sie w limicie centrali - {problem}");
    }

    if (tooLong.Count > 0)
    {
        return 1;
    }

    if (args.Contains("--submission"))
    {
        // A preview for reading, so the key stays masked here; --submit sends the real one.
        Console.WriteLine(ToolCatalog.BuildSubmissionPayload("***", settings.TaskName, baseUrl));
        Console.WriteLine();
        Console.WriteLine($"Opisy: {ToolCatalog.OffersDescription.Length} i {ToolCatalog.CitiesWithAllDescription.Length} znakow (limit {ToolCatalog.MaxDescriptionLength}).");
        Console.WriteLine($"To jest podglad. Wysylka: --submit --base-url {baseUrl}");
        return 0;
    }

    var submitKey = RequireApiKey(settings);
    if (submitKey is null)
    {
        return 1;
    }

    using var submitClient = new HubClient(settings.VerifyUrl, log);
    var submission = ToolCatalog.BuildSubmissionPayload(submitKey, settings.TaskName, baseUrl);
    var (submitStatus, submitBody) = await submitClient.PostAsync("submit", submission);
    PrintHubAnswer(submitStatus, submitBody);
    Console.WriteLine();
    Console.WriteLine("Agent centrali potrzebuje 30-60 s. Potem: --check");
    return submitStatus == 200 ? 0 : 1;
}

if (args.Contains("--check"))
{
    var checkKey = RequireApiKey(settings);
    if (checkKey is null)
    {
        return 1;
    }

    using var checkClient = new HubClient(settings.VerifyUrl, log);
    var checkPayload = ToolCatalog.BuildCheckPayload(checkKey, settings.TaskName);
    var (checkStatus, checkBody) = await checkClient.PostAsync("check", checkPayload);
    PrintHubAnswer(checkStatus, checkBody);
    return checkStatus == 200 ? 0 : 1;
}

if (!args.Contains("--serve"))
{
    PrintUsage();
    return 0;
}

var portIndex = Array.IndexOf(args, "--port");
var port = portIndex >= 0 && portIndex + 1 < args.Length && int.TryParse(args[portIndex + 1], out var parsed) ? parsed : 3000;

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var app = builder.Build();
app.MapNegotiationTools(offersTool, citiesWithAllTool, log);

Console.WriteLine($"Nasluchuje na http://localhost:{port}");
Console.WriteLine($"  POST {ToolRoutes.Offers}          - miasta oferujace jeden przedmiot");
Console.WriteLine($"  POST {ToolRoutes.CitiesWithAll}  - miasta majace cala liste naraz");
app.Run();
return 0;

static string? ReadBaseUrl(string[] arguments)
{
    var index = Array.IndexOf(arguments, "--base-url");
    if (index < 0 || index + 1 >= arguments.Length)
    {
        return null;
    }

    var value = arguments[index + 1];
    return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? value : null;
}

static string? RequireApiKey(TaskSettings taskSettings)
{
    var key = taskSettings.ResolveApiKey();
    if (!string.IsNullOrWhiteSpace(key))
    {
        return key;
    }

    Console.Error.WriteLine("Brak klucza. Ustaw Negotiations:AI_DevsApiKey w appsettings.Development.json albo zmienna AI_DEVS_API_KEY.");
    return null;
}

static void PrintToolAnswer(string output)
{
    Console.WriteLine(output);
    Console.WriteLine();
    Console.WriteLine($"[{ToolOutput.WireLength(output)} B na drucie, limit {ToolOutput.MaxBytes}]");
}

static void PrintHubAnswer(int statusCode, string body)
{
    Console.WriteLine($"HTTP {statusCode}");
    Console.WriteLine(body);

    var flag = Regex.Match(body, @"\{\{?FLG:[^}]*\}\}?");
    if (flag.Success)
    {
        Console.WriteLine();
        Console.WriteLine($"FLAGA: {flag.Value}  (w logu zapisana jako ***)");
    }
}

static void PrintUsage()
{
    Console.WriteLine("Uzycie:");
    Console.WriteLine("  --tests                              testy offline (bez sieci i klucza)");
    Console.WriteLine("  --query \"<opis>\"                     odpowiedz narzedzia dla jednego przedmiotu");
    Console.WriteLine("  --common \"<lista>\"                   odpowiedz narzedzia dla calej listy");
    Console.WriteLine("  --serve [--port 3000]                publiczne API obu narzedzi");
    Console.WriteLine("  --submission --base-url <adres>      podglad zgloszenia do centrali (bez wysylki)");
    Console.WriteLine("  --submit --base-url <adres>          zgloszenie narzedzi do centrali");
    Console.WriteLine("  --check                              odpytanie centrali o wynik i flage");
}
