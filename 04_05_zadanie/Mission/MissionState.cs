using _04_05_zadanie.Warehouse;

namespace _04_05_zadanie.Mission;

/// <summary>
/// Everything the run knows, kept by code: the demand, the facts read so far, the plan and the
/// last verdict. Acceptance is decided here from the validator's report, never from the model's
/// announcement, and any change to the plan withdraws it.
/// </summary>
public sealed class MissionState(Demand demand)
{
    public Demand Demand { get; } = demand;

    public Observations Observations { get; } = new();

    public OrderPlan Plan { get; } = new();

    public ValidationReport? LastReport { get; private set; }

    public bool Accepted { get; private set; }

    public int Queries { get; private set; }

    public int QueryRefusals { get; private set; }

    public int Registrations { get; private set; }

    public int RegistrationRefusals { get; private set; }

    public void CountQuery(bool refused)
    {
        if (refused) QueryRefusals++;
        else Queries++;
    }

    public void CountRegistration(bool refused)
    {
        if (refused) RegistrationRefusals++;
        else Registrations++;
        Accepted = false;
    }

    public ValidationReport Check()
    {
        LastReport = PlanValidator.Validate(Plan, Demand, Observations);
        return LastReport;
    }

    /// <summary>Accepts the plan when the last check found no errors; warnings need the caller's explicit consent.</summary>
    public bool TryAccept(bool acceptWarnings)
    {
        if (LastReport is not { IsValid: true } report)
            return false;
        if (report.Warnings.Count > 0 && !acceptWarnings)
            return false;

        Accepted = true;
        return true;
    }

    public string RenderProgress() =>
        $"[{Observations.RenderSummary()} Plan: {Plan.Orders.Count}/{Demand.Cities.Count} cities registered" +
        $"{(Plan.Orders.Count < Demand.Cities.Count ? $" (missing: {string.Join(", ", Demand.Cities.Where(c => Plan.Find(c.City) is null).Select(c => c.City))})" : string.Empty)}.]";
}
