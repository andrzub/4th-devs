namespace _05_02_zadanie.Call;

/// <summary>
/// What the call knows and what it has already said, built from readings of the operator's own turns.
/// A road status is recorded once and never quietly overwritten by a later turn that contradicts it:
/// a contradiction is visible as <see cref="Contradictions"/>, so a call cannot drift onto a road that
/// was cleared and then withdrawn.
/// </summary>
public sealed class CallState(IReadOnlyList<string> roads)
{
    private readonly Dictionary<string, RoadStatus> _statuses = [];
    private readonly List<string> _contradictions = [];
    private readonly HashSet<string> _disableRequestedFor = [];
    private readonly HashSet<string> _monitoringOff = [];

    public bool Introduced { get; private set; }
    public bool RoadQuestionAsked { get; private set; }
    public int PasswordsGiven { get; private set; }
    public int JustificationsGiven { get; private set; }
    public int ImprovisedTurns { get; private set; }
    public int DisableRequests { get; private set; }
    public bool Burned { get; private set; }
    public int TurnsTaken { get; private set; }

    /// <summary>What we said last, so a repeat of the request is not sent before the operator has had a turn to act on it.</summary>
    public TurnKind? LastDecisionKind { get; private set; }

    public OperatorReading LastReading { get; private set; } = OperatorReading.Silent;

    public IReadOnlyList<string> Contradictions => _contradictions;

    public IReadOnlyDictionary<string, RoadStatus> Statuses => _statuses;

    /// <summary>Roads the operator called passable, in the script's order.</summary>
    public IReadOnlyList<string> PassableRoads => roads.Where(road => _statuses.GetValueOrDefault(road) == RoadStatus.Passable).ToList();

    public bool AllRoadsAnswered => roads.All(road => _statuses.ContainsKey(road));

    public bool DisableRequested => _disableRequestedFor.Count > 0;

    public IReadOnlyCollection<string> MonitoringOff => _monitoringOff;

    /// <summary>The mission is done when every road we asked to clear is confirmed off.</summary>
    public bool MonitoringConfirmed => _disableRequestedFor.Count > 0 && _disableRequestedFor.All(_monitoringOff.Contains);

    public void Record(OperatorReading reading)
    {
        LastReading = reading;
        Burned |= reading.CallBurned;

        foreach (var road in reading.Roads.Where(road => road.Status != RoadStatus.Unknown))
        {
            if (_statuses.TryGetValue(road.Code, out var known) && known != road.Status)
                _contradictions.Add($"{road.Code}: first heard as {known}, now as {road.Status} (\"{road.Quote}\")");
            else
                _statuses[road.Code] = road.Status;
        }

        foreach (var road in reading.MonitoringDisabled)
            _monitoringOff.Add(road);
    }

    public void RecordSaid(TurnDecision decision)
    {
        TurnsTaken++;
        LastDecisionKind = decision.Kind;
        switch (decision.Kind)
        {
            case TurnKind.Introduce:
                Introduced = true;
                break;
            case TurnKind.AskRoadStatus:
                RoadQuestionAsked = true;
                break;
            case TurnKind.GivePassword:
                PasswordsGiven++;
                break;
            case TurnKind.Justify:
                JustificationsGiven++;
                break;
            case TurnKind.Improvise:
                ImprovisedTurns++;
                break;
            case TurnKind.RequestDisable:
                DisableRequests++;
                foreach (var road in decision.Roads)
                    _disableRequestedFor.Add(road);
                break;
        }
    }

    public string Describe()
    {
        var known = _statuses.Count == 0 ? "none" : string.Join(", ", roads.Where(_statuses.ContainsKey).Select(road => $"{road}={_statuses[road]}"));
        var off = _monitoringOff.Count == 0 ? "none" : string.Join(", ", _monitoringOff);
        return $"turn {TurnsTaken}; roads: {known}; monitoring off: {off}"
             + $"{(Burned ? "; BURNED" : "")}{(_contradictions.Count > 0 ? $"; {_contradictions.Count} contradiction(s)" : "")}";
    }
}
