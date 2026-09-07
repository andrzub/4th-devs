using System.Text;
using _02_04_zadanie.Llm;
using _02_04_zadanie.Mailbox;
using _02_04_zadanie.Mission;
using _02_04_zadanie.Tools;

namespace _02_04_zadanie.Agents;

/// <summary>
/// Builds and runs one researcher per assignment. Each gets a fresh context, its own report
/// tool and its own transcript file, while the mailbox client, the body cache and the
/// blackboard stay shared. That is the whole point of delegation here: the coordinator's
/// context never carries a single mail body, only the conclusions drawn from them.
/// </summary>
public sealed class ResearcherRunner(
    OpenAiCompatibleLlmClient client,
    ZmailClient zmail,
    MessageStore store,
    MissionState state,
    string apiHelp,
    string runDirectory,
    int maxIterations,
    int maxMessageBodyChars)
{
    private int _launched;

    public int Launched => Volatile.Read(ref _launched);

    public async Task<ResearcherResult> RunAsync(ResearchAssignment assignment, CancellationToken cancellationToken = default)
    {
        var number = Interlocked.Increment(ref _launched);
        var topic = assignment.Fact is { } fact ? FactNames.ToApiName(fact) : "recon";
        var label = $"researcher#{number}({topic})";

        var reportTool = new ReportFindingTool(assignment, state, label);
        var tools = new List<ITool>
        {
            new SearchMailTool(zmail),
            new GetInboxTool(zmail),
            new GetThreadTool(zmail),
            new GetMessagesTool(store, zmail, maxMessageBodyChars),
            reportTool
        };

        var transcript = new Transcript(Path.Combine(runDirectory, $"{label.Replace('#', '-').Replace("(", "-").Replace(")", "")}.txt"));
        var loop = new AgentLoop(label, client, ResearcherPrompt.Build(apiHelp), tools,
            () => reportTool.Report is not null, transcript, maxIterations, maxNudges: 2);

        Console.WriteLine($"[coordinator]   delegating to {label}");
        var run = await loop.RunAsync(BuildTask(assignment), _ =>
            "You have not called report_finding yet, so the coordinator has received nothing. Either keep searching " +
            "or report what you have, including a negative result with the queries you tried.", cancellationToken);

        return new ResearcherResult(label, reportTool.Report, run);
    }

    private string BuildTask(ResearchAssignment assignment)
    {
        var sb = new StringBuilder();

        if (assignment.Fact is { } fact)
        {
            sb.AppendLine($"Find one value: {FactNames.ToApiName(fact)}.");
            sb.AppendLine($"Required format: {DescribeFormat(fact)}");
        }
        else
        {
            sb.AppendLine("Reconnaissance assignment: no single value to extract. Report what you find in your notes.");
        }

        sb.AppendLine();
        sb.AppendLine("Briefing from the coordinator:");
        sb.AppendLine(assignment.Briefing);

        if (assignment.ExcludeValues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Already submitted and rejected as wrong, so do not report any of these: " +
                          string.Join(", ", assignment.ExcludeValues.Select(v => $"\"{v}\"")));
        }

        sb.AppendLine();
        sb.AppendLine(zmail.BudgetLine);
        return sb.ToString().TrimEnd();
    }

    private static string DescribeFormat(MissionFact fact) => fact switch
    {
        MissionFact.Date => "YYYY-MM-DD, a calendar date. Convert a Polish date written in words or in another order into this format, but never work out a date the mail does not state.",
        MissionFact.Password => "the password exactly as written, nothing else: no label, no quotes, no trailing punctuation.",
        MissionFact.ConfirmationCode => $"\"SEC-\" followed by 32 characters, {AnswerValidator.ConfirmationCodeLength} characters in total. Copy it whole; a code of any other length is the wrong one or was copied incompletely.",
        _ => "as written."
    };
}
