using System.Text.Json.Nodes;
using _04_02_zadanie.Hub;

namespace _04_02_zadanie.Mission;

/// <summary>
/// Drains the API's queue in the background and hands each finished item to whoever is waiting for
/// it. getResult returns one item at a time, in the order the queue happens to finish them, and an
/// item served is an item gone - so a single poller collects everything and the rest of the run
/// awaits items by name instead of racing for them.
/// </summary>
public sealed class QueuePump(WindPowerClient hub, Action<string, HubReply> record)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(120);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, JsonObject> _collected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TaskCompletionSource<JsonObject>> _waiting = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Collected
    {
        get
        {
            lock (_gate)
                return _collected.Keys.ToArray();
        }
    }

    public bool Has(string key)
    {
        lock (_gate)
            return _collected.ContainsKey(key);
    }

    public async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HubReply reply;
            try
            {
                reply = await hub.GetResultAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (JsonNode.Parse(reply.Body) is not JsonObject item || item["sourceFunction"]?.ToString() is not { } source)
            {
                try
                {
                    await Task.Delay(PollInterval, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            var key = KeyOf(source, item);
            record($"collect {key}", reply);
            Publish(key, item);
        }
    }

    /// <summary>
    /// Waits for one named item. A timeout is not a failure to report upwards blindly: the caller
    /// re-orders the work, because the queue was observed to accept an order and never finish it.
    /// </summary>
    public async Task<JsonObject?> WaitAsync(string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task<JsonObject> pending;

        lock (_gate)
        {
            if (_collected.TryGetValue(key, out var ready))
                return ready;

            if (!_waiting.TryGetValue(key, out var source))
            {
                source = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting[key] = source;
            }

            pending = source.Task;
        }

        var finished = await Task.WhenAny(pending, Task.Delay(timeout, cancellationToken));
        return finished == pending ? await pending : null;
    }

    /// <summary>
    /// The key an item is filed under: its source function for reports, and the configuration point
    /// it signs for a signature. The generator echoes what it signed, which is what makes ordering
    /// every signature at once possible at all.
    /// </summary>
    private static string KeyOf(string source, JsonObject item)
    {
        if (item["signedParams"]?.AsObject() is not { } signed)
            return source;

        var date = signed["startDate"]?.ToString();
        var hour = signed["startHour"]?.ToString();

        return date is null || hour is null ? source : $"{date} {hour}";
    }

    private void Publish(string key, JsonObject item)
    {
        TaskCompletionSource<JsonObject>? waiter;

        lock (_gate)
        {
            _collected[key] = item;
            _waiting.Remove(key, out waiter);
        }

        waiter?.TrySetResult(item);
    }
}
