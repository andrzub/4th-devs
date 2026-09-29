using _05_02_zadanie.Hub;
using _05_02_zadanie.Voice;

namespace _05_02_zadanie.Call;

public sealed record CallOutcome(string? Flag, string Ending, int TurnsSpoken, CallState State, string? Hint);

/// <summary>
/// The call itself: hear the operator, decide the next turn, let the guard refuse it, say it, repeat. Every
/// recording it sends was verified before the call started, except a line written mid-call, which is verified
/// the same way before it goes out.
/// </summary>
public sealed class Conversation(
    IOperatorChannel channel,
    CallScript script,
    VoiceBank bank,
    VoiceStudio studio,
    VoiceSettings voiceSettings,
    OperatorReader reader,
    Improviser improviser,
    CallRecorder recorder,
    bool stepByStep,
    int maxTurns = 12)
{
    private const int MaxImprovisationAttempts = 2;

    private readonly TurnPlanner _planner = new();
    private readonly TurnGuard _guard = new(script);

    public async Task<CallOutcome> RunAsync(CancellationToken cancellationToken = default)
    {
        var recordings = bank.Load();
        var state = new CallState(script.Roads);

        var start = await channel.StartAsync(cancellationToken);
        recorder.SaveReply(0, "start", start);
        Console.WriteLine($"start: {start.Preview()}");
        if (!start.IsSuccess)
            return new CallOutcome(null, "the hub would not open a call", 0, state, start.Hint);

        state.Record(await ReadOperatorAsync(start, 0, cancellationToken));
        string? lastHint = start.Hint;

        for (var turn = 1; turn <= maxTurns; turn++)
        {
            var decision = _planner.Decide(state);
            if (decision.Kind is TurnKind.Finish or TurnKind.Abort)
                return new CallOutcome(null, $"{decision.Kind.ToString().ToLowerInvariant()}: {decision.Reason}", turn - 1, state, lastHint);

            var line = await ResolveLineAsync(decision, state, turn, cancellationToken);
            if (line is null)
                return new CallOutcome(null, "the guard refused every version of this turn", turn - 1, state, lastHint);

            var audio = await RecordingForAsync(line, recordings, turn, cancellationToken);
            if (audio is null)
                return new CallOutcome(null, $"no recording of '{line.Key}' that a listener could hear", turn - 1, state, lastHint);

            Console.WriteLine($"{Environment.NewLine}turn {turn} [{decision.Kind}] {decision.Reason}");
            Console.WriteLine($"  we say: \"{line.Text}\"");
            if (stepByStep && !Confirm())
                return new CallOutcome(null, "stopped at the step prompt", turn - 1, state, lastHint);

            recorder.AppendTranscript("caller", line.Text);
            var reply = await channel.SayAsync(audio, line.Text, cancellationToken);
            state.RecordSaid(decision);
            recorder.SaveReply(turn, decision.Kind.ToString().ToLowerInvariant(), reply);
            lastHint = reply.Hint ?? lastHint;
            ReportHubVerdict(reply);

            if (reply.Flag is { } flag)
            {
                recorder.AppendTranscript("hub", $"FLAG {flag}");
                return new CallOutcome(flag, "the hub returned a flag", turn, state, lastHint);
            }

            state.Record(await ReadOperatorAsync(reply, turn, cancellationToken));
            Console.WriteLine($"  state: {state.Describe()}");
            foreach (var contradiction in state.Contradictions)
                Console.WriteLine($"  WARNING: {contradiction}");
        }

        return new CallOutcome(null, $"the call ran out of turns ({maxTurns})", maxTurns, state, lastHint);
    }

    /// <summary>
    /// Everything the hub says about the turn besides the operator's voice: the stage code it advanced to, the
    /// transcription it made of our recording, and — on a rejection — the hint naming what the turn was missing.
    /// This is the task's only documentation, so none of it is swallowed.
    /// </summary>
    private void ReportHubVerdict(HubReply reply)
    {
        if (reply.Code is { } code)
            Console.WriteLine($"  hub: code {code}{(reply.Message is { Length: > 0 } message ? $" — {message}" : "")}");

        if (reply.HeardFromUs is { Length: > 0 } heard)
        {
            recorder.AppendTranscript("hub-heard-us", heard);
            Console.WriteLine($"  hub heard us say: \"{heard}\"");
        }

        if (reply.Hint is { Length: > 0 } hint)
        {
            recorder.AppendTranscript("hub-hint", hint);
            Console.WriteLine($"  HINT: {hint}");
        }
    }

    /// <summary>The scripted line for this turn, or a line written and guarded on the spot. Null when nothing passed the guard.</summary>
    private async Task<ScriptLine?> ResolveLineAsync(TurnDecision decision, CallState state, int turn, CancellationToken cancellationToken)
    {
        if (decision.Kind != TurnKind.Improvise)
        {
            var scripted = script.LineFor(decision)!;
            var verdict = _guard.Check(decision, state);
            if (verdict.Allowed)
                return scripted;

            Console.WriteLine($"  guard refused [{decision.Kind}]: {verdict.Refusal}");
            return null;
        }

        var instruction = improviser.InstructionFor(state, decision);
        for (var attempt = 1; attempt <= MaxImprovisationAttempts; attempt++)
        {
            var text = await improviser.WriteAsync(state, instruction, cancellationToken);
            var verdict = _guard.Check(decision, state, text);
            if (verdict.Allowed)
                return script.Improvised(text, turn);

            Console.WriteLine($"  guard refused an improvised line (attempt {attempt}/{MaxImprovisationAttempts}): {verdict.Refusal} -> \"{text}\"");
        }

        return null;
    }

    private async Task<byte[]?> RecordingForAsync(ScriptLine line, IReadOnlyDictionary<string, BankEntry> recordings, int turn, CancellationToken cancellationToken)
    {
        // The key alone is not enough: a line whose wording changed since the bank was prepared must be recorded again.
        if (recordings.TryGetValue(line.Key, out var prepared) && prepared.Text == line.Text)
            return await File.ReadAllBytesAsync(prepared.RecordingPath, cancellationToken);

        var entry = await bank.PrepareLineAsync(line, cancellationToken: cancellationToken);
        Console.WriteLine($"  recorded on the spot, take {entry.Take}: {(entry.Passed ? "heard by every listener" : "NOT heard correctly")}");
        if (!entry.Passed)
            return null;

        recorder.AppendTranscript("caller-recording", $"turn {turn} take {entry.Take}: {entry.RecordingPath}");
        return await File.ReadAllBytesAsync(entry.RecordingPath, cancellationToken);
    }

    /// <summary>What the operator's turn said: transcripts of whatever it carried as audio, or its Polish text when it carried none.</summary>
    private async Task<OperatorReading> ReadOperatorAsync(HubReply reply, int turn, CancellationToken cancellationToken)
    {
        var recordings = reply.FindAudio();
        var words = new List<string>();

        for (var index = 0; index < recordings.Count; index++)
        {
            var audio = recordings[index];
            var path = recorder.SaveOperatorAudio(turn, index, audio);
            words.Add(await studio.HearAsync(audio.Bytes, Path.GetFileName(path), voiceSettings.TranscriptionModel, script.VocabularyHint, cancellationToken));
        }

        if (words.Count == 0 && ReplyText(reply) is { Length: > 0 } text)
            words.Add(text);

        var spoken = string.Join(" ", words).Trim();
        if (spoken.Length == 0)
            return OperatorReading.Silent;

        recorder.AppendTranscript("operator", spoken);
        Console.WriteLine($"  operator: \"{spoken}\"");

        var reading = await reader.ReadAsync(spoken, cancellationToken);
        if (reading.Summary.Length > 0)
            Console.WriteLine($"  reading: {reading.Summary}");

        return reading;
    }

    /// <summary>The hub puts the operator's Polish in "msg" and its own English status in "message".</summary>
    private static string ReplyText(HubReply reply) =>
        (reply.Text("msg") ?? reply.Message ?? "").Trim();

    private static bool Confirm()
    {
        Console.Write("  [Enter] send, anything else stops the call: ");
        return Console.ReadLine() is { Length: 0 };
    }
}
