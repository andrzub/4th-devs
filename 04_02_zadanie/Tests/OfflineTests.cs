using System.Globalization;
using System.Text.Json.Nodes;
using _04_02_zadanie.Analysis;
using _04_02_zadanie.Mission;

namespace _04_02_zadanie.Tests;

/// <summary>
/// Everything that decides the answer, checked without a network or a key. The service window is
/// forty seconds long and cannot be paused, so every rule that would otherwise be discovered by
/// losing a window - which hours are storms, which hour covers the deficit, whether a signature
/// belongs to the point it is filed under - is settled here instead.
/// </summary>
public static class OfflineTests
{
    /// <summary>The documentation exactly as the API serves it, so the power curve is tested against the real wording.</summary>
    private const string Documentation = """
        {
          "code": 50,
          "ratedPowerKw": 14,
          "windPowerYieldPercent": [
            { "windMs": 4, "yieldPercent": "10-15" },
            { "windMs": 6, "yieldPercent": "30-40" },
            { "windMs": 8, "yieldPercent": "60-70" },
            { "windMs": 10, "yieldPercent": "90-100" },
            { "windMsRange": "12-14", "yieldPercent": "100" },
            { "windMsRange": "14+", "yieldPercent": "damage" }
          ],
          "pitchAngleYieldPercent": [
            { "pitchAngleDeg": 0, "yieldPercent": "100" },
            { "pitchAngleDeg": 45, "yieldPercent": "65" },
            { "pitchAngleDeg": 90, "yieldPercent": "0" }
          ],
          "safety": { "cutoffWindMs": 14, "minOperationalWindMs": 4 }
        }
        """;

    public static bool Run()
    {
        var failures = 0;
        var total = 0;

        void Check(string name, Action assertion)
        {
            total++;
            try
            {
                assertion();
                Console.WriteLine($"  pass  {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"  FAIL  {name}: {exception.Message}");
            }
        }

        var turbine = TurbineModel.Parse(Documentation);

        // -- the machine ------------------------------------------------------------------

        Check("documentation yields the rated power, the cutoff and the three pitch angles", () =>
        {
            Expect(turbine.RatedPowerKw == 14, $"rated {turbine.RatedPowerKw}");
            Expect(turbine.CutoffWindMs == 14, $"cutoff {turbine.CutoffWindMs}");
            Expect(turbine.MinOperationalWindMs == 4, $"minimum {turbine.MinOperationalWindMs}");
            Expect(turbine.AllowedPitchAngles.SequenceEqual([0, 45, 90]), "pitch angles");
            Expect(turbine.ProtectivePitchAngle == 90, "protective pitch");
            Expect(turbine.ProductivePitchAngle == 0, "productive pitch");
        });

        Check("wind below the operational floor produces nothing", () =>
            Expect(turbine.PowerKw(3.9, 0).Max == 0, "3.9 m/s should yield nothing"));

        Check("wind at or above the cutoff produces nothing and counts as damage", () =>
        {
            Expect(turbine.IsDamaging(14), "14 m/s must count as damaging");
            Expect(turbine.IsDamaging(28), "28 m/s must count as damaging");
            Expect(!turbine.IsDamaging(13.9), "13.9 m/s must not");
            Expect(turbine.PowerKw(25, 0).Max == 0, "a storm yields nothing");
        });

        Check("the published yields are interpolated between the table's anchors", () =>
        {
            var at6 = turbine.PowerKw(6, 0);
            Expect(Close(at6.Min, 4.2) && Close(at6.Max, 5.6), $"6 m/s gave {at6}");

            var at66 = turbine.PowerKw(6.6, 0);
            Expect(Close(at66.Min, 5.46) && Close(at66.Max, 6.86), $"6.6 m/s gave {at66}");

            var at49 = turbine.PowerKw(4.9, 0);
            Expect(Close(at49.Min, 2.66) && Close(at49.Max, 3.675), $"4.9 m/s gave {at49}");
        });

        Check("feathering the blades removes the power, 45 degrees only reduces it", () =>
        {
            Expect(turbine.PowerKw(6.6, 90).Max == 0, "pitch 90 must yield nothing");
            Expect(Close(turbine.PowerKw(6.6, 45).Max, 6.86 * 0.65), "pitch 45 is 65% of pitch 0");
        });

        // -- the two sessions actually observed --------------------------------------------

        Check("observed session with 6.6 m/s and a 4-5 kW deficit", () =>
        {
            var plan = SchedulePlanner.Plan(Session(6.6), turbine, new ValueRange(4, 5));

            Expect(plan.Protections.Count == 3, $"{plan.Protections.Count} storms protected");
            Expect(plan.Production?.Key == "2026-09-22 20:00:00", $"production at {plan.Production?.Key}");
            Expect(plan.Production?.PitchAngle == 0 && plan.Production?.TurbineMode == TurbineModes.Production, "production settings");
            Expect(plan.Protections.All(point => point.PitchAngle == 90 && point.TurbineMode == TurbineModes.Idle), "protection settings");
            Expect(ScheduleValidator.Validate(plan, Session(6.6), turbine).Accepted, "the plan should validate");
        });

        Check("observed session with 4.9 m/s and a 2-3 kW deficit still finds the hour", () =>
        {
            var plan = SchedulePlanner.Plan(Session(4.9), turbine, new ValueRange(2, 3));

            Expect(plan.Production?.Key == "2026-09-22 20:00:00", $"production at {plan.Production?.Key}");
            Expect(ScheduleValidator.Validate(plan, Session(4.9), turbine).Accepted, "the plan should validate");
            Expect(plan.Notes.Any(note => note.Contains("short of")), "the thin worst case should be stated out loud");
        });

        Check("the storms are protected even though they are the windiest hours in the week", () =>
        {
            var plan = SchedulePlanner.Plan(Session(4.9), turbine, new ValueRange(2, 3));
            var storms = new[] { "2026-09-22 18:00:00", "2026-09-25 18:00:00", "2026-09-26 18:00:00" };

            foreach (var storm in storms)
                Expect(plan.Points.Any(point => point.Key == storm && point.PitchAngle == 90), $"{storm} unprotected");
        });

        Check("an hour exactly at the cutoff is protected and the contradiction is reported", () =>
        {
            var forecast = Forecast((Moment("2026-09-22 18:00:00"), 14), (Moment("2026-09-22 20:00:00"), 6.6));
            var plan = SchedulePlanner.Plan(forecast, turbine, new ValueRange(4, 5));

            Expect(plan.Protections.Count == 1, "the cutoff hour must be protected");
            Expect(plan.Notes.Any(note => note.Contains("cutoff")), "the disagreement must be noted");
        });

        Check("no usable hour yields no production point rather than a wrong one", () =>
        {
            var forecast = Forecast((Moment("2026-09-22 18:00:00"), 25), (Moment("2026-09-22 20:00:00"), 3.2));
            var plan = SchedulePlanner.Plan(forecast, turbine, new ValueRange(4, 5));

            Expect(plan.Production is null, "there is no hour that can cover the deficit");
            Expect(!ScheduleValidator.Validate(plan, forecast, turbine).Accepted, "a plan with no production must be refused");
        });

        // -- the guard in front of the one config call --------------------------------------

        Check("a storm left unconfigured is refused", () =>
        {
            var forecast = Session(6.6);
            var full = SchedulePlanner.Plan(forecast, turbine, new ValueRange(4, 5));
            var missing = new SchedulePlan(
                full.Points.Where(point => point.Key != "2026-09-25 18:00:00").ToList(),
                full.Production,
                full.Protections.Where(point => point.Key != "2026-09-25 18:00:00").ToList(),
                full.Deficit,
                full.Notes);

            var verdict = ScheduleValidator.Validate(missing, forecast, turbine);
            Expect(!verdict.Accepted && verdict.Problems.Any(problem => problem.Contains("2026-09-25 18:00:00")), "the exposed storm must be named");
        });

        Check("a storm configured for production is refused", () =>
        {
            var forecast = Session(6.6);
            var reckless = Plan(forecast, new ValueRange(4, 5),
                new ConfigPoint(Moment("2026-09-22 18:00:00"), 25, 0, TurbineModes.Production));

            Expect(!ScheduleValidator.Validate(reckless, forecast, turbine).Accepted, "producing in a storm must be refused");
        });

        Check("a production point too weak for the deficit is refused", () =>
        {
            var forecast = Session(6.6);
            var weak = Plan(forecast, new ValueRange(4, 5),
                new ConfigPoint(Moment("2026-09-22 18:00:00"), 25, 90, TurbineModes.Idle),
                new ConfigPoint(Moment("2026-09-25 18:00:00"), 22, 90, TurbineModes.Idle),
                new ConfigPoint(Moment("2026-09-26 18:00:00"), 28, 90, TurbineModes.Idle),
                new ConfigPoint(Moment("2026-09-22 20:00:00"), 6.6, 45, TurbineModes.Production));

            var verdict = ScheduleValidator.Validate(weak, forecast, turbine);
            Expect(!verdict.Accepted && verdict.Problems.Any(problem => problem.Contains("at best")), "pitch 45 cannot cover 5 kW at 6.6 m/s");
        });

        Check("a later production hour is refused while an earlier one would do", () =>
        {
            var forecast = Session(6.6);
            var late = Plan(forecast, new ValueRange(4, 5),
                new ConfigPoint(Moment("2026-09-22 18:00:00"), 25, 90, TurbineModes.Idle),
                new ConfigPoint(Moment("2026-09-25 18:00:00"), 22, 90, TurbineModes.Idle),
                new ConfigPoint(Moment("2026-09-26 18:00:00"), 28, 90, TurbineModes.Idle),
                new ConfigPoint(Moment("2026-09-24 20:00:00"), 6.6, 0, TurbineModes.Production));

            var verdict = ScheduleValidator.Validate(late, forecast, turbine);
            Expect(!verdict.Accepted && verdict.Problems.Any(problem => problem.Contains("first possible moment")), "the centre asked for the first moment");
        });

        Check("a point signed for a wind the forecast does not report is refused", () =>
        {
            var forecast = Session(4.9);
            var stale = Plan(forecast, new ValueRange(2, 3),
                new ConfigPoint(Moment("2026-09-22 18:00:00"), 25, 90, TurbineModes.Idle),
                new ConfigPoint(Moment("2026-09-25 18:00:00"), 22, 90, TurbineModes.Idle),
                new ConfigPoint(Moment("2026-09-26 18:00:00"), 28, 90, TurbineModes.Idle),
                new ConfigPoint(Moment("2026-09-22 20:00:00"), 6.6, 0, TurbineModes.Production));

            var verdict = ScheduleValidator.Validate(stale, forecast, turbine);
            Expect(!verdict.Accepted, "a signature for last session's wind must not be stored");
        });

        Check("a point without its signature is refused", () =>
        {
            var forecast = Session(6.6);
            var plan = SchedulePlanner.Plan(forecast, turbine, new ValueRange(4, 5));
            var partial = plan.Points.Take(2).ToDictionary(point => point.Key, _ => "0123456789abcdef0123456789abcdef");

            var verdict = ScheduleValidator.Validate(plan, forecast, turbine, partial);
            Expect(!verdict.Accepted && verdict.Problems.Any(problem => problem.Contains("unlockCode")), "unsigned points must be named");
        });

        Check("half past the hour is refused, the API wants whole hours", () =>
        {
            var forecast = Session(6.6);
            var plan = Plan(forecast, new ValueRange(4, 5), new ConfigPoint(Moment("2026-09-22 20:30:00"), 6.6, 0, TurbineModes.Production));

            Expect(ScheduleValidator.Validate(plan, forecast, turbine).Problems.Any(problem => problem.Contains("full hour")), "the half hour must be caught");
        });

        // -- matching signatures to their points ---------------------------------------------

        Check("a signature is filed under the point its signedParams describe", () =>
        {
            var points = new[]
            {
                new ConfigPoint(Moment("2026-09-22 18:00:00"), 25, 90, TurbineModes.Idle),
                new ConfigPoint(Moment("2026-09-22 20:00:00"), 6.6, 0, TurbineModes.Production)
            };

            var collector = new SignatureCollector(points);
            var accepted = collector.Accept(Signature("2026-09-22", "20:00:00", "6.6", "0.0", "d2eb89dff850b7d29f7488c779f0390e"));

            Expect(accepted?.Key == "2026-09-22 20:00:00", $"filed under {accepted?.Key}");
            Expect(collector.Signatures["2026-09-22 20:00:00"] == "d2eb89dff850b7d29f7488c779f0390e", "wrong code stored");
            Expect(!collector.Complete, "the other point is still unsigned");
        });

        Check("a signature that identifies nothing is held back rather than guessed", () =>
        {
            var points = new[]
            {
                new ConfigPoint(Moment("2026-09-22 18:00:00"), 25, 90, TurbineModes.Idle),
                new ConfigPoint(Moment("2026-09-25 18:00:00"), 22, 90, TurbineModes.Idle)
            };

            var collector = new SignatureCollector(points);
            var bare = new JsonObject { ["sourceFunction"] = "unlockCodeGenerator", ["unlockCode"] = "ffffffffffffffffffffffffffffffff" };

            Expect(collector.Accept(bare) is null, "an unidentifiable code must not be assigned");
            Expect(collector.Signatures.Count == 0, "nothing may be stored");
            Expect(collector.ForceSinglePending() is null, "two points are pending, so forcing is unsafe");
        });

        Check("the last pending point may claim the last unidentified signature", () =>
        {
            var points = new[] { new ConfigPoint(Moment("2026-09-22 18:00:00"), 25, 90, TurbineModes.Idle) };
            var collector = new SignatureCollector(points);
            collector.Accept(new JsonObject { ["sourceFunction"] = "unlockCodeGenerator", ["unlockCode"] = "ffffffffffffffffffffffffffffffff" });

            Expect(collector.ForceSinglePending()?.Key == "2026-09-22 18:00:00", "the only pending point should take it");
            Expect(collector.Complete, "the collector should now be complete");
        });

        // -- small parts that the rest leans on -----------------------------------------------

        Check("ranges are read as the API writes them", () =>
        {
            Expect(ValueRange.Parse("4-5") == new ValueRange(4, 5), "4-5");
            Expect(ValueRange.Parse("100") == new ValueRange(100, 100), "100");
            Expect(ValueRange.Parse("10-15") == new ValueRange(10, 15), "10-15");
            Expect(!ValueRange.TryParse("damage", out _), "damage is not a range");
        });

        Check("a configuration point formats the fields the API asks for", () =>
        {
            var point = new ConfigPoint(Moment("2026-09-22 20:00:00"), 6.6, 0, TurbineModes.Production);

            Expect(point.StartDate == "2026-09-22", point.StartDate);
            Expect(point.StartHour == "20:00:00", point.StartHour);
            Expect(point.Key == "2026-09-22 20:00:00", point.Key);
        });

        Check("a forecast is recognised as the same one only when every hour matches", () =>
        {
            Expect(Session(6.6).MatchesWind(Session(6.6)), "identical forecasts must match");
            Expect(!Session(6.6).MatchesWind(Session(4.9)), "a changed reading must not match");
        });

        Console.WriteLine();
        Console.WriteLine($"{total - failures}/{total} checks passed.");
        return failures == 0;
    }

    /// <summary>
    /// The shape both observed sessions had: a becalmed week, three storms at identical speeds and
    /// two candidate hours whose wind is redrawn per session. Only the candidate speed varies here,
    /// because that is the one thing that differed between the windows actually opened.
    /// </summary>
    private static WeatherForecast Session(double candidateWind) => Forecast(
        (Moment("2026-09-21 00:00:00"), 3.9),
        (Moment("2026-09-21 12:00:00"), 2.6),
        (Moment("2026-09-22 18:00:00"), 25),
        (Moment("2026-09-22 20:00:00"), candidateWind),
        (Moment("2026-09-23 12:00:00"), 2.2),
        (Moment("2026-09-24 20:00:00"), candidateWind),
        (Moment("2026-09-25 18:00:00"), 22),
        (Moment("2026-09-26 18:00:00"), 28),
        (Moment("2026-09-27 22:00:00"), 3.1));

    private static WeatherForecast Forecast(params (DateTime Timestamp, double WindMs)[] entries) =>
        new(entries.Select(entry => new ForecastEntry(entry.Timestamp, entry.WindMs, 0, 20)).OrderBy(entry => entry.Timestamp).ToArray(), 2);

    private static SchedulePlan Plan(WeatherForecast forecast, ValueRange deficit, params ConfigPoint[] points)
    {
        var ordered = points.OrderBy(point => point.Timestamp).ToList();

        return new SchedulePlan(
            ordered,
            ordered.FirstOrDefault(point => point.TurbineMode == TurbineModes.Production),
            ordered.Where(point => point.TurbineMode == TurbineModes.Idle).ToList(),
            deficit,
            []);
    }

    private static JsonObject Signature(string date, string hour, string wind, string pitch, string code) => new()
    {
        ["sourceFunction"] = "unlockCodeGenerator",
        ["unlockCode"] = code,
        ["signedParams"] = new JsonObject
        {
            ["startDate"] = date,
            ["startHour"] = hour,
            ["windMs"] = wind,
            ["pitchAngle"] = pitch
        }
    };

    private static DateTime Moment(string text) => DateTime.ParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static bool Close(double actual, double expected) => Math.Abs(actual - expected) < 0.01;

    private static void Expect(bool condition, string detail)
    {
        if (!condition)
            throw new InvalidOperationException(detail);
    }
}
