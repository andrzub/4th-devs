using _01_01_zadanie.Models;
using System.Text;
using System.Text.Json.Nodes;

namespace _01_01_zadanie.Services;

public sealed class OpenAiPersonTaggingService
{
    private const string ResponseSchema = """
        {
            "type": "object",
            "properties": {
                "people": {
                    "type": "array",
                    "description": "Classification results for each input person in the same order as the input.",
                    "items": {
                        "type": "object",
                        "properties": {
                            "inputIndex": {
                                "type": "integer",
                                "description": "Index copied exactly from the input item."
                            },
                            "tags": {
                                "type": "array",
                                "description": "One or more tags assigned from the allowed enum values. Use 'unknown' only if none of the other tags fit.",
                                "items": {
                                    "type": "string",
                                    "enum": [
                                        "IT",
                                        "transport",
                                        "edukacja",
                                        "medycyna",
                                        "praca z ludümi",
                                        "praca z pojazdami",
                                        "praca fizyczna",
                                        "unknown"
                                    ]
                                },
                                "minItems": 1
                            }
                        },
                        "required": ["inputIndex", "tags"],
                        "additionalProperties": false
                    }
                }
            },
            "required": ["people"],
            "additionalProperties": false
        }
        """;

    private readonly HttpClient _client;
    private readonly string _model;

    public OpenAiPersonTaggingService(HttpClient client, string model)
    {
        _client = client;
        _model = model;
    }

    public async Task<IReadOnlyList<TaggedPerson>> TagPeopleAsync(IReadOnlyList<Person> people, int batchSize, CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        var indexedPeople = people
            .Select((person, index) => new IndexedPerson(index, person))
            .ToList();

        var taggedPeople = new List<TaggedPerson>(indexedPeople.Count);

        foreach (var batch in indexedPeople.Chunk(batchSize))
        {
            var batchItems = batch.ToList();
            var taggingResults = await TagBatchAsync(batchItems, cancellationToken);

            ValidateBatch(batchItems, taggingResults);

            var tagsByIndex = taggingResults.ToDictionary(result => result.InputIndex, result => result.Tags);

            foreach (var indexedPerson in batchItems)
            {
                taggedPeople.Add(new TaggedPerson(
                    indexedPerson.Person.Name,
                    indexedPerson.Person.Surname,
                    indexedPerson.Person.Gender,
                    indexedPerson.Person.BirthDate.Year,
                    indexedPerson.Person.BirthPlace,
                    tagsByIndex[indexedPerson.InputIndex]));
            }
        }

        return taggedPeople;
    }

    private async Task<List<BatchTaggingResult>> TagBatchAsync(IReadOnlyList<IndexedPerson> batch, CancellationToken cancellationToken)
    {
        var requestBody = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = BuildPrompt(batch)
                }
            },
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "person_tags",
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(ResponseSchema)
                }
            }
        };

        using var response = await _client.PostAsync(
            "https://api.openai.com/v1/chat/completions",
            new StringContent(requestBody.ToJsonString(), Encoding.UTF8, "application/json"),
            cancellationToken);

        var rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenAI request failed with status code {(int)response.StatusCode}: {rawResponse}");
        }

        var responseJson = JsonNode.Parse(rawResponse)
            ?? throw new InvalidOperationException("OpenAI response body could not be parsed.");

        var messageContent = responseJson["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
            ?? throw new InvalidOperationException("OpenAI response did not contain structured content.");

        var contentJson = JsonNode.Parse(messageContent)
            ?? throw new InvalidOperationException("Structured response content could not be parsed.");

        var people = contentJson["people"]?.AsArray()
            ?? throw new InvalidOperationException("Structured response did not contain the people array.");

        return people
            .Select(person => new BatchTaggingResult(
                person?["inputIndex"]?.GetValue<int>()
                    ?? throw new InvalidOperationException("Structured response item is missing inputIndex."),
                person["tags"]?.AsArray().Select(tag => tag?.GetValue<string>()
                    ?? throw new InvalidOperationException("Structured response item contains an invalid tag."))
                    .ToList()
                    ?? throw new InvalidOperationException("Structured response item is missing tags.")))
            .ToList();
    }

    private static string BuildPrompt(IEnumerable<IndexedPerson> batch)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("You will classify each person based only on the provided JobDescription.");
        prompt.AppendLine("Return exactly one result for each input person.");
        prompt.AppendLine("Preserve the same order as the input.");
        prompt.AppendLine("Do not skip any person.");
        prompt.AppendLine("Do not add any extra person.");
        prompt.AppendLine("Copy inputIndex exactly from the input.");
        prompt.AppendLine("Use tag values exactly as provided in the schema.");
        prompt.AppendLine("A person may have multiple tags if the job clearly fits multiple categories.");
        prompt.AppendLine("Use 'unknown' only if none of the other tags fit.");
        prompt.AppendLine("Do not use 'unknown' together with any other tag.");
        prompt.AppendLine("Do not repeat the same tag for one person.");
        prompt.AppendLine("Do not infer tags from name, surname, birth date, or city.");
        prompt.AppendLine("Assign tags using these rules:");
        prompt.AppendLine("- IT: information technology, programming, software, systems, data, administration.");
        prompt.AppendLine("- transport: transport of people or goods, logistics, delivery, shipping, fleet management.");
        prompt.AppendLine("- edukacja: teaching, training, tutoring, education.");
        prompt.AppendLine("- medycyna: healthcare, treatment, diagnosis, nursing, emergency medicine.");
        prompt.AppendLine("- praca z ludümi: direct work with people, clients, patients, customers, service, sales, support.");
        prompt.AppendLine("- praca z pojazdami: driving, vehicle maintenance, mechanics, repair, technical work on vehicles.");
        prompt.AppendLine("- praca fizyczna: manual labor, construction, warehouse work, field work, physically demanding work.");
        prompt.AppendLine("If a description is vague, choose only tags strongly supported by the text.");
        prompt.AppendLine("Here are the people to classify:");

        foreach (var indexedPerson in batch)
        {
            prompt.AppendLine($"InputIndex: {indexedPerson.InputIndex}, Name: {indexedPerson.Person.Name}, Surname: {indexedPerson.Person.Surname}, BirthDate: {indexedPerson.Person.BirthDate:yyyy-MM-dd}, JobDescription: {indexedPerson.Person.Job}");
        }

        return prompt.ToString();
    }

    private static void ValidateBatch(IReadOnlyCollection<IndexedPerson> batch, IReadOnlyCollection<BatchTaggingResult> taggingResults)
    {
        if (taggingResults.Count != batch.Count)
        {
            throw new InvalidOperationException($"Expected {batch.Count} tagging results, but got {taggingResults.Count}.");
        }

        var expectedIndexes = batch.Select(person => person.InputIndex).ToHashSet();
        var actualIndexes = taggingResults.Select(result => result.InputIndex).ToList();

        if (actualIndexes.Count != actualIndexes.Distinct().Count())
        {
            throw new InvalidOperationException("Structured response contains duplicate inputIndex values.");
        }

        var missingIndexes = expectedIndexes.Except(actualIndexes).ToList();
        var unexpectedIndexes = actualIndexes.Where(index => !expectedIndexes.Contains(index)).ToList();

        if (missingIndexes.Count != 0 || unexpectedIndexes.Count != 0)
        {
            throw new InvalidOperationException(
                $"Structured response indexes do not match the batch. Missing: [{string.Join(", ", missingIndexes)}], Unexpected: [{string.Join(", ", unexpectedIndexes)}].");
        }

        foreach (var taggingResult in taggingResults)
        {
            if (taggingResult.Tags.Count != taggingResult.Tags.Distinct(StringComparer.Ordinal).Count())
            {
                throw new InvalidOperationException($"Structured response contains duplicate tags for inputIndex {taggingResult.InputIndex}.");
            }

            if (taggingResult.Tags.Contains("unknown", StringComparer.Ordinal) && taggingResult.Tags.Count > 1)
            {
                throw new InvalidOperationException($"Structured response contains 'unknown' together with other tags for inputIndex {taggingResult.InputIndex}.");
            }
        }
    }

    private sealed record IndexedPerson(int InputIndex, Person Person);

    private sealed record BatchTaggingResult(int InputIndex, List<string> Tags);
}
