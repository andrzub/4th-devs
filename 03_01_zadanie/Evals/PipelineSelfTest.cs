using _03_01_zadanie.Anomalies;
using _03_01_zadanie.Notes;
using _03_01_zadanie.Sensors;

namespace _03_01_zadanie.Evals;

/// <summary>
/// Offline checks for the parts of the pipeline that involve no model: how a batch reply is
/// parsed, whether the control statements actually catch a lazy answer, how clause verdicts
/// fold into a note verdict, and how data faults and note tone combine into the four anomaly
/// definitions from the task. Runs without an API key or a network connection, so the
/// machinery around the classifier can be trusted before a single token is paid for.
/// </summary>
public static class PipelineSelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        var passed = 0;

        void Check(string name, bool condition)
        {
            if (condition)
            {
                passed++;
                Console.WriteLine($"  pass  {name}");
            }
            else
            {
                failures.Add(name);
                Console.WriteLine($"  FAIL  {name}");
            }
        }

        Console.WriteLine("Reply parsing:");
        var wellFormed = ClassificationReply.Parse("PROBLEM: 2, 5\nUNCLEAR: 7", 10);
        Check("well-formed reply parses", wellFormed.Error is null);
        Check("flagged items keep their verdict", wellFormed.ToneOf(2) == NoteTone.Problem && wellFormed.ToneOf(7) == NoteTone.Unclear);
        Check("unlisted items default to Ok", wellFormed.ToneOf(1) == NoteTone.Ok);
        Check("empty lines are legal", ClassificationReply.Parse("PROBLEM:\nUNCLEAR:", 10).Error is null);
        Check("'none' is accepted as empty", ClassificationReply.Parse("PROBLEM: none\nUNCLEAR: -", 10).Error is null);
        Check("lower case headers are accepted", ClassificationReply.Parse("problem: 1\nunclear:", 10).ToneOf(1) == NoteTone.Problem);
        Check("a missing line is rejected", ClassificationReply.Parse("PROBLEM: 1", 10).Error is not null);
        Check("an empty reply is rejected", ClassificationReply.Parse("", 10).Error is not null);
        Check("an out-of-range item is rejected", ClassificationReply.Parse("PROBLEM: 11\nUNCLEAR:", 10).Error is not null);
        Check("a non-numeric item is rejected", ClassificationReply.Parse("PROBLEM: item two\nUNCLEAR:", 10).Error is not null);
        Check("an item in both lines is rejected", ClassificationReply.Parse("PROBLEM: 3\nUNCLEAR: 3", 10).Error is not null);

        Console.WriteLine();
        Console.WriteLine("Control statements:");
        var batch = ClassificationBatch.Create(["everything is calm", "values are steady", "sample logged"], seed: 1);
        Check("controls are added to the batch", batch.Items.Count == batch.StatementCount + ClassificationBatch.Controls.Length);
        Check("real statement count excludes controls", batch.StatementCount == 3);

        var honest = HonestReplyFor(batch);
        Check("an honest reply is accepted", batch.Verify(honest) is null);
        Check("verdicts are collected for real statements only", batch.CollectVerdicts(honest).Count == 3);
        Check("a reply that flags nothing is rejected", batch.Verify(ClassificationReply.Parse("PROBLEM:\nUNCLEAR:", batch.Items.Count)) is not null);

        var everythingFlagged = string.Join(',', Enumerable.Range(1, batch.Items.Count));
        Check("a reply that flags everything is rejected", batch.Verify(ClassificationReply.Parse($"PROBLEM: {everythingFlagged}\nUNCLEAR:", batch.Items.Count)) is not null);

        var withControlText = ClassificationBatch.Create([ClassificationBatch.Controls[0].Text, "values are steady"], seed: 1);
        Check("a control is not duplicated when it is also a real statement",
            withControlText.Items.Count(item => item.Text == ClassificationBatch.Controls[0].Text) == 1);

        Console.WriteLine();
        Console.WriteLine("Note decomposition:");
        const string templated = "Everything checks out, nothing suggests a fault condition, and the status stays green for this routine audit.";
        Check("a templated note splits into its clauses", NoteDecomposer.Split(templated).Count == 3);
        Check("a note without commas stays whole", NoteDecomposer.Split("The report looks completely normal. I will check the other devices.").Count == 1);
        Check("trailing periods are trimmed off clauses", NoteDecomposer.Split(templated).All(clause => !clause.EndsWith('.')));
        Check("one worried clause decides the note", NoteDecomposer.Combine([NoteTone.Ok, NoteTone.Problem, NoteTone.Ok]) == NoteTone.Problem);
        Check("a healthy clause outweighs an empty one", NoteDecomposer.Combine([NoteTone.Ok, NoteTone.Unclear]) == NoteTone.Ok);
        Check("nothing but empty clauses stays unclear", NoteDecomposer.Combine([NoteTone.Unclear, NoteTone.Unclear]) == NoteTone.Unclear);

        Console.WriteLine();
        Console.WriteLine("Anomaly definitions:");
        Check("a healthy file with a healthy note is not an anomaly",
            !VerdictFor("temperature", temperature: 700, tone: NoteTone.Ok).IsAnomaly);

        Check("a value outside its range is an anomaly",
            VerdictFor("temperature", temperature: 1101, tone: NoteTone.Problem).IsAnomaly);

        Check("an inactive channel reporting data is an anomaly",
            VerdictFor("temperature", temperature: 700, water: 5.2, tone: NoteTone.Ok).IsAnomaly);

        var signedOff = VerdictFor("voltage", voltage: 304.5, tone: NoteTone.Ok);
        Check("a faulty file signed off as healthy is an anomaly", signedOff.IsAnomaly);
        Check("the sign-off is named in the reasons", signedOff.Reasons.Any(reason => reason.Contains("signed the reading off")));

        var falseAlarm = VerdictFor("temperature", temperature: 700, tone: NoteTone.Problem);
        Check("a healthy file with a worried note is an anomaly", falseAlarm.IsAnomaly);
        Check("the false alarm is named in the reasons", falseAlarm.Reasons.Any(reason => reason.Contains("every channel is within range")));

        var silentNote = VerdictFor("temperature", temperature: 700, tone: NoteTone.Unclear);
        Check("a healthy file with a silent note is not submitted", !silentNote.IsAnomaly);
        Check("a healthy file with a silent note is raised for review", silentNote.NeedsHumanReview);

        Check("an unknown sensor type is an anomaly",
            VerdictFor("radiation", tone: NoteTone.Ok).IsAnomaly);

        Console.WriteLine();
        Console.WriteLine(failures.Count == 0
            ? $"All {passed} checks passed."
            : $"{failures.Count} of {passed + failures.Count} checks failed: {string.Join("; ", failures)}");

        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>Flags the planted problem, and only it — what a careful model would reply.</summary>
    private static ClassificationReply HonestReplyFor(ClassificationBatch batch)
    {
        var problemPositions = batch.Items
            .Select((item, index) => (item, number: index + 1))
            .Where(entry => entry.item.Expected == NoteTone.Problem)
            .Select(entry => entry.number);

        return ClassificationReply.Parse($"PROBLEM: {string.Join(',', problemPositions)}{Environment.NewLine}UNCLEAR:", batch.Items.Count);
    }

    private static FileVerdict VerdictFor(string sensorType, double temperature = 0, double pressure = 0, double water = 0, double voltage = 0, double humidity = 0, NoteTone tone = NoteTone.Ok)
    {
        var json = $$"""
            {
              "sensor_type": "{{sensorType}}",
              "timestamp": 1774064280,
              "temperature_K": {{temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
              "pressure_bar": {{pressure.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
              "water_level_meters": {{water.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
              "voltage_supply_v": {{voltage.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
              "humidity_percent": {{humidity.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
              "operator_notes": "self test"
            }
            """;

        var reading = SensorReading.Parse("0000", json);

        return new FileVerdict
        {
            Reading = reading,
            DataFaults = ReadingValidator.Validate(reading),
            Tone = tone
        };
    }
}
