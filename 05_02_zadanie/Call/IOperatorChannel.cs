using _05_02_zadanie.Hub;

namespace _05_02_zadanie.Call;

/// <summary>
/// The other end of the line. The hub hears only the recording; <paramref name="spokenText"/> is what the
/// recording says, and only a simulator reads it.
/// </summary>
public interface IOperatorChannel
{
    Task<HubReply> StartAsync(CancellationToken cancellationToken = default);

    Task<HubReply> SayAsync(byte[] recording, string spokenText, CancellationToken cancellationToken = default);
}
