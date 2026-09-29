namespace _05_02_zadanie.Voice;

/// <summary>The OpenAI audio endpoints that speak our lines and hear both sides of the call, bound from the "Voice" section.</summary>
public class VoiceSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    public string SpeechModel { get; set; } = "gpt-4o-mini-tts";
    public string Voice { get; set; } = "marin";

    /// <summary>Delivery instructions for the synthesizer. Only the gpt-4o-*-tts models accept them, so leave empty for tts-1.</summary>
    public string SpeechInstructions { get; set; } = string.Empty;

    /// <summary>Hears the operator during the call.</summary>
    public string TranscriptionModel { get; set; } = "gpt-4o-transcribe";

    /// <summary>
    /// Independent listeners every recorded line must pass before a call may start. Starts empty for the
    /// same binder reason as <see cref="TaskSettings.Roads"/>; an empty list is refused, never read as "nothing to check".
    /// A listener weaker than the hub's own recogniser rejects audio the hub hears perfectly well, so
    /// <c>whisper-1</c> is deliberately not among them: it writes the Polish name in the script as "Siegfried".
    /// </summary>
    public List<string> CheckModels { get; set; } = [];

    /// <summary>How many times a line is synthesized before the bank gives up on it.</summary>
    public int MaxTakes { get; set; } = 5;

    public string Language { get; set; } = "pl";
}
