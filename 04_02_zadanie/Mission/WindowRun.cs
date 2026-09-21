using System.Diagnostics;
using System.Text.Json.Nodes;
using _04_02_zadanie.Analysis;
using _04_02_zadanie.Hub;

namespace _04_02_zadanie.Mission;

public enum WindowMode
{
    /// <summary>Everything except storing and validating: the window is spent proving the choreography.</summary>
    Rehearsal,

    /// <summary>The full errand, ending in config and done.</summary>
    Full
}

/// <summary>
/// The service window, from "start" to "done", in one deterministic pass.
///
/// Every number the schedule rests on is read inside this window, because the forecast and the
/// plant's deficit are regenerated per session - only the documentation and the storms themselves
/// survive between them. The order of work follows the measured cost of each report rather than
/// the order the task describes them in: the weather report alone takes twenty-four of the forty
/// seconds, so it is ordered first and everything cheap is arranged to happen while it is pending.
/// </summary>
public sealed class WindowRun(WindPowerClient hub, TurbineModel turbine, string directory)
{
    /// <summary>Time held back after the forecast arrives for signatures, config and done.</summary>
    private static readonly TimeSpan SubmissionReserve = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan SignaturePatience = TimeSpan.FromSeconds(3);

    private readonly Stopwatch _clock = new();
    private readonly Lock _recordGate = new();

    public async Task<bool> RunAsync(WindowMode mode, ValueRange? cachedDeficit, TimeSpan budget, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);

        _clock.Start();
        var start = await hub.StartAsync(cancellationToken);
        Record("start", start);

        if (!start.IsSuccess)
        {
            Console.WriteLine("The service window did not open; nothing else is worth trying.");
            return false;
        }

        using var stopPump = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pump = new QueuePump(hub, Record);
        var pumping = pump.PollAsync(stopPump.Token);

        try
        {
            return await WorkAsync(pump, mode, cachedDeficit, budget, cancellationToken);
        }
        finally
        {
            await stopPump.CancelAsync();
            await pumping;
        }
    }

    private async Task<bool> WorkAsync(QueuePump pump, WindowMode mode, ValueRange? cachedDeficit, TimeSpan budget, CancellationToken cancellationToken)
    {
        // Orders go out one after another rather than all at once. Two orders three milliseconds
        // apart were seen to leave one of them queued forever, and a request costs ~35 ms, so
        // sending them in sequence is both the cheapest fix and its own spacing. The second copy
        // of each report is insurance: losing the forecast loses the window.
        await OrderReportAsync("weather", cancellationToken);
        await OrderReportAsync("powerplantcheck", cancellationToken);
        await OrderReportAsync("turbinecheck", cancellationToken);
        await OrderReportAsync("weather", cancellationToken);
        await OrderReportAsync("powerplantcheck", cancellationToken);

        var deadline = budget - SubmissionReserve;

        // The forecast is the last thing to arrive by a wide margin, so it sets the pace; the plant
        // report is due fourteen seconds earlier and only gets a short grace period after it.
        var weatherReport = await pump.WaitAsync("weather", Remaining(deadline), cancellationToken);
        var plantReport = await pump.WaitAsync("powerplantcheck", TimeSpan.FromSeconds(2), cancellationToken);

        if (weatherReport is null)
        {
            Console.WriteLine($"[{_clock.Elapsed.TotalSeconds:F1}s] No forecast arrived in time; the window is abandoned without storing anything.");
            return false;
        }

        var forecast = WeatherForecast.Parse(weatherReport.ToJsonString());
        var deficit = ReadDeficit(plantReport, cachedDeficit);

        if (deficit is null)
        {
            Console.WriteLine($"[{_clock.Elapsed.TotalSeconds:F1}s] The plant never reported its deficit and nothing is cached; the production hour cannot be chosen.");
            return false;
        }

        var plan = SchedulePlanner.Plan(forecast, turbine, deficit.Value);

        Console.WriteLine();
        Console.WriteLine($"[{_clock.Elapsed.TotalSeconds:F1}s] Schedule from this session's own data (deficit {deficit.Value} kW):");
        foreach (var point in plan.Points)
            Console.WriteLine($"    {point}");
        foreach (var note in plan.Notes)
            Console.WriteLine($"    note: {note}");

        var shape = ScheduleValidator.Validate(plan, forecast, turbine);
        if (!shape.Accepted)
        {
            Console.WriteLine("The schedule does not hold up against the forecast it came from, so nothing is stored:");
            foreach (var problem in shape.Problems)
                Console.WriteLine($"  - {problem}");
            return false;
        }

        var signatures = await CollectSignaturesAsync(pump, plan, budget, cancellationToken);
        if (signatures is null)
            return false;

        var verdict = ScheduleValidator.Validate(plan, forecast, turbine, signatures);
        if (!verdict.Accepted)
        {
            Console.WriteLine("The signed schedule failed its final check, so nothing is stored:");
            foreach (var problem in verdict.Problems)
                Console.WriteLine($"  - {problem}");
            return false;
        }

        if (!pump.Has("turbinecheck"))
            Console.WriteLine($"[{_clock.Elapsed.TotalSeconds:F1}s] Warning: the turbine check was ordered but never came back; done is documented to need it.");

        if (mode == WindowMode.Rehearsal)
        {
            Console.WriteLine();
            Console.WriteLine($"[{_clock.Elapsed.TotalSeconds:F1}s] Rehearsal stops here. Configuration that would be stored:");
            foreach (var point in plan.Points)
                Console.WriteLine($"    {point}   code {signatures[point.Key]}");
            return true;
        }

        var stored = await hub.ConfigAsync(plan.Points, signatures, cancellationToken);
        Record("config", stored);

        if (!stored.IsSuccess)
        {
            Console.WriteLine("The API rejected the configuration, so done is not sent - it would only validate an empty schedule.");
            return false;
        }

        Record("done", await hub.DoneAsync(cancellationToken));
        return true;
    }

    /// <summary>
    /// Orders one signature per point and waits for them. A signature that does not come back is
    /// re-ordered rather than waited out: the queue accepts an order and silently drops it often
    /// enough to have happened once in three windows, and a second order costs one request.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>?> CollectSignaturesAsync(QueuePump pump, SchedulePlan plan, TimeSpan budget, CancellationToken cancellationToken)
    {
        var collector = new SignatureCollector(plan.Points);

        foreach (var point in plan.Points)
            Record($"order code {point.Key}", await hub.UnlockCodeAsync(point, cancellationToken));

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var patience = attempt == 1 ? SignaturePatience : Remaining(budget - TimeSpan.FromSeconds(1));

            foreach (var point in collector.Missing.ToList())
            {
                if (await pump.WaitAsync(point.Key, patience, cancellationToken) is { } reply)
                    collector.Accept(reply);
            }

            if (collector.Complete)
                return collector.Signatures;

            foreach (var point in collector.Missing.ToList())
                Record($"re-order code {point.Key}", await hub.UnlockCodeAsync(point, cancellationToken));
        }

        Console.WriteLine($"[{_clock.Elapsed.TotalSeconds:F1}s] Still unsigned: {string.Join(", ", collector.Missing.Select(point => point.Key))}");
        Console.WriteLine("Nothing is stored: a point without its own code is rejected anyway, and a partial schedule leaves the blades exposed.");
        return null;
    }

    private ValueRange? ReadDeficit(JsonObject? plantReport, ValueRange? cached)
    {
        if (plantReport is not null && ValueRange.TryParse(plantReport["powerDeficitKw"]?.ToString(), out var fresh))
            return fresh;

        if (cached is null)
            return null;

        Console.WriteLine($"[{_clock.Elapsed.TotalSeconds:F1}s] Warning: falling back to the cached deficit of {cached} kW - this session never reported its own.");
        return cached;
    }

    private async Task OrderReportAsync(string report, CancellationToken cancellationToken) =>
        Record($"order {report}", await hub.GetAsync(report, cancellationToken));

    private TimeSpan Remaining(TimeSpan deadline)
    {
        var left = deadline - _clock.Elapsed;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    private void Record(string label, HubReply reply)
    {
        lock (_recordGate)
        {
            Console.WriteLine($"  [{_clock.Elapsed.TotalSeconds,5:F1}s] {label,-34} HTTP {reply.Status}  ({reply.Elapsed.TotalMilliseconds:F0} ms)  {Preview(reply.Body)}");
            File.WriteAllText(Path.Combine(directory, $"{_clock.ElapsedMilliseconds:D6}-{Sanitise(label)}.json"), reply.Body);
        }
    }

    private static string Preview(string body)
    {
        var flat = string.Join(' ', body.Split('\n', '\r').Select(line => line.Trim()).Where(line => line.Length > 0));
        return flat.Length <= 120 ? flat : flat[..120] + "...";
    }

    private static string Sanitise(string label) => string.Concat(label.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
}
