using System.Text.Json;
using _01_04_zadanie.Documents;
using _01_04_zadanie.Llm;

namespace _01_04_zadanie.Tools;

/// <summary>
/// The agent's eyes. Part of the SPK documentation exists only as graphics, and the agent driving
/// the loop never sees them — it passes a file name here and gets back an answer in text.
/// The image goes to the model as a base64 data URL built by <see cref="DocumentLibrary"/>.
/// </summary>
public sealed class AnalyzeImageTool : ITool
{
    private const string TranscriberInstruction = """
        You are reading a page of technical documentation that was supplied as an image.
        Answer the question strictly from what is visible. Transcribe tables faithfully, row by row,
        preserving codes and numbers exactly as printed. If something the question asks for is not
        visible in the image, say so plainly instead of inferring it.
        """;

    private readonly DocumentLibrary _library;
    private readonly ILlmClient _llm;
    private readonly string _visionModel;

    public AnalyzeImageTool(DocumentLibrary library, ILlmClient llm, string visionModel)
    {
        _library = library;
        _llm = llm;
        _visionModel = visionModel;
    }

    public string Name => "analyze_image";

    public string Description =>
        "Ask a question about a graphic from the SPK documentation (.png/.jpg). The image is read by a " +
        "vision model and the answer comes back as text. Use it for any file referenced by the documentation " +
        "that is not a text file — some data exists nowhere else. Ask for a full transcription when the graphic is a table.";

    public JsonElement ParametersSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "file": {
              "type": "string",
              "description": "Bare image file name from the documentation directory, e.g. 'trasy-wylaczone.png'."
            },
            "question": {
              "type": "string",
              "description": "What you need to learn from this image. Be specific."
            }
          },
          "required": ["file", "question"],
          "additionalProperties": false
        }
        """);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        string file, question;
        try
        {
            using var args = JsonDocument.Parse(argumentsJson);
            file = args.RootElement.GetProperty("file").GetString() ?? "";
            question = args.RootElement.GetProperty("question").GetString() ?? "";
        }
        catch (Exception ex)
        {
            return $"Could not read the arguments: {ex.Message}";
        }

        if (!DocumentLibrary.IsImage(file))
            return $"'{file}' is not a graphic. Use fetch_document to read it as text.";

        try
        {
            var dataUrl = await _library.ReadAsDataUrlAsync(file, cancellationToken);

            var response = await _llm.CompleteAsync(new LlmRequest
            {
                Model = _visionModel,
                Messages =
                [
                    Message.System(TranscriberInstruction),
                    Message.UserWithImages(question, [dataUrl])
                ]
            }, cancellationToken);

            return response.Content ?? response.ContentRaw ?? "(the vision model returned no content)";
        }
        catch (Exception ex)
        {
            return $"Could not analyze '{file}': {ex.Message}";
        }
    }
}
