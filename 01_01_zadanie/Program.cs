using _01_01_zadanie.Services;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .Build();

var apiKey = configuration["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("OpenAI:ApiKey is not set in appsettings.json.");

var client = new HttpClient();
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

var csvReader = new PersonCsvReader();
var candidateSelector = new TransportCandidateSelector();
var taggingService = new OpenAiPersonTaggingService(client, "gpt-5.4");

var people = csvReader.Read("people.csv");
var peopleRange = candidateSelector.Select(people, currentYear: 2026);
var taggedPeople = await taggingService.TagPeopleAsync(peopleRange, batchSize: 10);

var transportPeople = taggedPeople
    .Where(person => person.Tags.Any(tag => string.Equals(tag, "transport", StringComparison.Ordinal)))
    .ToList();

var transportPeopleJson = JsonSerializer.Serialize(transportPeople, new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
});

Console.WriteLine(transportPeopleJson);
