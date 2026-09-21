using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using _04_02_zadanie.Hub;

namespace _04_02_zadanie.Mission;

/// <summary>
/// One reconnaissance pass through a service window: open it, order every report at once, drain
/// the queue and write down what came back and when. Nothing is configured and nothing is
/// submitted - the window is spent on learning the shape of the data and the real cost in seconds,
/// so the run that does configure the turbine can be planned against measurements instead of guesses.
/// </summary>
public static class Recon
{
    public static readonly string[] QueuedReports = ["weather", "turbinecheck", "powerplantcheck"];

    public static async Task RunAsync(WindPowerClient hub, string directory, TimeSpan budget, bool openWindow, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);

        var clock = Stopwatch.StartNew();
        var outstanding = new HashSet<string>(QueuedReports, StringComparer.OrdinalIgnoreCase);

        void Record(string label, HubReply reply)
        {
            lock (outstanding)
            {
                Console.WriteLine($"  [{clock.Elapsed.TotalSeconds,5:F1}s] {label,-24} HTTP {reply.Status}  ({reply.Elapsed.TotalMilliseconds:F0} ms)  {Preview(reply.Body)}");
                File.WriteAllText(Path.Combine(directory, $"{clock.ElapsedMilliseconds:D6}-{Sanitise(label)}.json"), reply.Body);
            }
        }

        if (openWindow)
        {
            Console.WriteLine("Opening the service window...");
            Record("start", await hub.StartAsync(cancellationToken));
        }
        else
        {
            Console.WriteLine("Probing the queue without opening a service window...");
        }

        var orders = new[] { "documentation", "weather", "turbinecheck", "powerplantcheck" }
            .Select(async param =>
            {
                var reply = await hub.GetAsync(param, cancellationToken);
                Record($"get {param}", reply);

                // An order the API refused will never reach the queue, so stop waiting for it -
                // otherwise the drain hammers an empty queue until the budget runs out.
                if (!reply.IsSuccess)
                    lock (outstanding)
                        outstanding.Remove(param);
            });

        var drain = DrainAsync(hub, outstanding, budget, clock, Record, cancellationToken);

        await Task.WhenAll(orders.Append(drain));

        Console.WriteLine();
        Console.WriteLine($"Elapsed: {clock.Elapsed.TotalSeconds:F1}s, {hub.RequestsSent} requests.");
        if (outstanding.Count > 0)
            Console.WriteLine($"Never collected: {string.Join(", ", outstanding)}");
        Console.WriteLine($"Raw responses: {directory}");
    }

    /// <summary>
    /// Keeps a few getResult calls in flight until every ordered report has been handed over.
    /// Each call pops one finished item, the order is random, and an item collected twice is gone -
    /// so the loop stops the moment the last outstanding source function shows up.
    /// </summary>
    private static async Task DrainAsync(
        WindPowerClient hub,
        HashSet<string> outstanding,
        TimeSpan budget,
        Stopwatch clock,
        Action<string, HubReply> record,
        CancellationToken cancellationToken)
    {
        const int Workers = 2;

        var workers = Enumerable.Range(1, Workers).Select(async worker =>
        {
            while (clock.Elapsed < budget)
            {
                lock (outstanding)
                {
                    if (outstanding.Count == 0)
                        return;
                }

                var reply = await hub.GetResultAsync(cancellationToken);
                var source = SourceFunction(reply.Body);
                record($"getResult#{worker} {source ?? "-"}", reply);

                if (source is not null)
                {
                    lock (outstanding)
                        outstanding.Remove(source);
                    continue;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        });

        await Task.WhenAll(workers);
    }

    private static string? SourceFunction(string body)
    {
        try
        {
            return JsonNode.Parse(body) is JsonObject root && root["sourceFunction"] is JsonValue value
                ? value.ToString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Preview(string body)
    {
        var flat = string.Join(' ', body.Split('\n', '\r').Select(line => line.Trim()).Where(line => line.Length > 0));
        return flat.Length <= 140 ? flat : flat[..140] + "...";
    }

    private static string Sanitise(string label) =>
        string.Concat(label.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
}
