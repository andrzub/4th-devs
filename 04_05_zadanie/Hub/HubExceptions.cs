namespace _04_05_zadanie.Hub;

/// <summary>
/// Raised when the run has spent the calls it was allowed to make to /verify. The agent loop ends
/// on it instead of handing the model an error it cannot do anything about.
/// </summary>
public sealed class HubBudgetExceededException(string message) : Exception(message);

/// <summary>
/// Raised when a call that must not be repeated (create, append) failed before a reply arrived.
/// Whether the hub processed it is unknown, so the caller reads the order back before deciding.
/// </summary>
public sealed class HubTransportException(string message, Exception inner) : Exception(message, inner);
