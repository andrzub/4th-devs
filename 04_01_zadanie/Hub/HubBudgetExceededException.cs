namespace _04_01_zadanie.Hub;

/// <summary>
/// Raised when the run has spent the calls it was allowed to make to /verify. The agent loop ends
/// on it instead of handing the model an error it cannot do anything about.
/// </summary>
public sealed class HubBudgetExceededException(string message) : Exception(message);
