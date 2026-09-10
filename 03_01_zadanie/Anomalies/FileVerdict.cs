using _03_01_zadanie.Notes;
using _03_01_zadanie.Sensors;

namespace _03_01_zadanie.Anomalies;

/// <summary>
/// The final judgement on one sensor file: what the numbers say, what the operator claims, and
/// whether the two together make the file an anomaly.
/// </summary>
public sealed class FileVerdict
{
    public required SensorReading Reading { get; init; }
    public required IReadOnlyList<DataFault> DataFaults { get; init; }
    public required NoteTone Tone { get; init; }

    public string Id => Reading.Id;
    public bool DataIsClean => DataFaults.Count == 0;

    /// <summary>
    /// An <see cref="NoteTone.Unclear"/> note over healthy data is deliberately not an anomaly:
    /// none of the four definitions in the task covers it. Such files are surfaced separately
    /// for a human to look at instead of being quietly added to the answer.
    /// </summary>
    public bool IsAnomaly => !DataIsClean || Tone == NoteTone.Problem;

    public bool NeedsHumanReview => DataIsClean && Tone == NoteTone.Unclear;

    public IReadOnlyList<string> Reasons
    {
        get
        {
            var reasons = DataFaults.Select(fault => fault.Describe()).ToList();

            if (!DataIsClean && Tone == NoteTone.Ok)
                reasons.Add("operator signed the reading off as healthy");
            else if (DataIsClean && Tone == NoteTone.Problem)
                reasons.Add("operator reports a problem while every channel is within range");
            else if (DataIsClean && Tone == NoteTone.Unclear)
                reasons.Add("operator note asserts nothing about the condition of the equipment");

            return reasons;
        }
    }

    /// <summary>Which of the task's anomaly definitions this file falls under.</summary>
    public string Category => (DataIsClean, Tone) switch
    {
        (false, NoteTone.Ok)      => "faulty data, operator says it is fine",
        (false, _)                => "faulty data",
        (true, NoteTone.Problem)  => "healthy data, operator reports a problem",
        (true, NoteTone.Unclear)  => "healthy data, note says nothing",
        _                          => "healthy"
    };
}
