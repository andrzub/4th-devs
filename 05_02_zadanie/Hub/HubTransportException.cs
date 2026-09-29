namespace _05_02_zadanie.Hub;

/// <summary>
/// Raised when a request that must not be repeated got no reply. Whether the operator heard the turn is
/// unknown, so the call stops here instead of guessing.
/// </summary>
public sealed class HubTransportException(string message, Exception inner) : Exception(message, inner);
