using System.Text;
using _05_01_zadanie.Analysis;
using _05_01_zadanie.Routing;

namespace _05_01_zadanie.Tests;

/// <summary>Router, decoders, area rounding and report validation on synthetic material. No network, no key.</summary>
public static class OfflineTests
{
    private static int _passed;
    private static int _failed;

    public static bool Run()
    {
        _passed = _failed = 0;

        const string noise = "kshhh ...odbierasz mnie... bzzzt ...tu patrol... trzask trzask ...cztery-siedem-dwa... pisk ...sygnał zanik... szzzz ...cztery-sied... ksssh ...odbiór jest...";
        const string speech = "Odwiedzałem ostatnio Syjon... powiem Ci, że tam jest naprawdę na bogato. **[niesłyszalne]** Mają dużo bydła.";
        Check("static is recognised as noise", NoiseFilter.IsNoise(noise));
        Check("speech with one ellipsis and an inaudible mark is not noise", !NoiseFilter.IsNoise(speech));
        var fragments = NoiseFilter.DistinctFragments([noise, noise]);
        Check("noise fragments are deduplicated across captures", fragments.Count(f => f == "odbierasz mnie") == 1);
        Check("a cut-off fragment gives way to its full form", fragments.Contains("cztery-siedem-dwa") && !fragments.Contains("cztery-sied"));
        Check("radio sounds are not fragments", !fragments.Any(f => f.Contains("bzz") || f.Contains("trzask")));

        Check("Morse syllables decode to letters and words",
            MorseDecoder.TryDecode("*shhh* TaTa TiTiTa TiTiTi TiTi TiTiTi TaTaTiTi (stop) TiTaTaTi TiTa *khhh*", out var morse) && morse == "MUSISZ PA", morse);
        Check("an unknown Morse sequence stays visible", MorseDecoder.TryDecode("Ti Ti Ti Ti TiTiTiTiTiTiTi", out var unknown) && unknown.Contains("[?......."), unknown);
        Check("ordinary speech is not Morse", !MorseDecoder.TryDecode(speech, out _));

        Check("area rounds half away from zero", CityTable.FormatArea(0.125m) == "0.13");
        Check("area rounds, does not truncate", CityTable.FormatArea(15.6861m) == "15.69");
        Check("area keeps two decimals", CityTable.FormatArea(3m) == "3.00");

        var png = Attachment(1, "text/plain", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0]);
        Check("a PNG labelled as text is routed to vision by its bytes", Router.Decide(png, 20000).Route == Route.Image);
        var big = Attachment(2, "text/csv", Encoding.UTF8.GetBytes(new string('a', 30000)));
        Check("a text file above the inline limit is skipped", Router.Decide(big, 20000).Route == Route.Skip);
        var unknownBinary = Attachment(3, "application/octet-stream", [0, 1, 2, 3, 0, 5]);
        Check("an unreadable binary is skipped, never pasted", Router.Decide(unknownBinary, 20000).Route == Route.Skip);

        var cityJson = Attachment(4, "application/json", Encoding.UTF8.GetBytes("""[ { "name": "Alfa", "occupiedArea": 1.005 }, { "name": "Beta", "occupiedArea": 2.5 } ]"""));
        var talk = Transcription(5, "Na Syjonie mają dwanaście magazynów, a numer do Tomka to 600 100 200.");
        var routed = new[] { Router.Decide(cityJson, 20000), Router.Decide(talk, 20000) };
        Check("JSON attachments are minified before the prompt", routed[0].LocalText!.Contains("\"name\":\"Alfa\""), routed[0].LocalText);
        var cities = CityTable.From(routed);
        Check("city records are read from JSON documents", cities.Find("alfa") is { Area: 1.005m });
        var corpus = EvidenceCorpus.Build(routed, []);

        var good = new AnalystFindings("Alfa", "", ["#005"], 12, "mają dwanaście magazynów", "600100200", "numer do Tomka to 600 100 200", "");
        var result = ReportValidator.Validate(good, corpus, cities);
        Check("valid findings become the transmit payload", result.IsValid && result.Report!.CityArea == "1.01" && result.Report.PhoneNumber == "600100200",
            string.Join("; ", result.Errors));
        Check("the payload carries the transmit action", result.Report?.ToAnswer()["action"]?.ToString() == "transmit");

        var invented = good with { PhoneQuote = "numer do Tomka to 600 999 999" };
        Check("an invented quote is refused", !ReportValidator.Validate(invented, corpus, cities).IsValid);
        var unknownCity = good with { CityName = "Gamma" };
        Check("a city without a record is refused", !ReportValidator.Validate(unknownCity, corpus, cities).IsValid);
        var spaced = good with { PhoneNumber = "600-100-200" };
        var normalised = ReportValidator.Validate(spaced, corpus, cities);
        Check("a phone with separators is normalised with a warning", normalised.IsValid && normalised.Report!.PhoneNumber == "600100200" && normalised.Warnings.Count > 0);

        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0;
    }

    private static Capture.Capture Attachment(int number, string meta, byte[] content) =>
        new(number, 100, "Signal captured.", Capture.CaptureKind.Attachment, null, meta, content.Length, content, $"capture-{number:D3}.json");

    private static Capture.Capture Transcription(int number, string text) =>
        new(number, 100, "Signal captured.", Capture.CaptureKind.Transcription, text, null, null, null, $"capture-{number:D3}.json");

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
            _passed++;
        else
        {
            _failed++;
            Console.WriteLine($"FAIL: {name}{(detail is null ? "" : $" -> {detail}")}");
        }
    }
}
