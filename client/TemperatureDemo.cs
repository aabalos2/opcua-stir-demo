using StirClient.Control;
using StirClient.Services;

namespace StirClient;

/// <summary>
/// Runs the temperature control-loop demo: the same plant (simulated
/// heater + sensor, with real thermal lag and constant heat loss to
/// ambient) driven first by deliberately bad gains, then by tuned gains,
/// then disturbed mid-run to show recovery. Each phase's trace is written
/// to a CSV so the actual numbers can be inspected or plotted afterward,
/// not just watched scroll by in the console.
/// </summary>
public static class TemperatureDemo
{
    private const double Setpoint = 50.0; // target temperature, degrees C
    private const double TickSeconds = 0.5;
    private static readonly TimeSpan TickDelay = TimeSpan.FromSeconds(TickSeconds);

    public static async Task RunAsync(Opc.Ua.ApplicationConfiguration appConfig, string serverUrl)
    {
        await using ITemperatureControlService plc = new TemperatureControlService(appConfig);
        await plc.ConnectAsync(serverUrl);

        Console.WriteLine("\n########## Temperature control-loop demo ##########");

        // === Phase 1: deliberately bad gains -- Kp way too high, no I or D. ===
        // Expect: the output saturates hard at 0 or 100, overshoots the
        // setpoint, and oscillates instead of settling.
        Console.WriteLine("\n=== Phase 1: BAD gains (Kp=20, Ki=0, Kd=0) -- watch for oscillation ===");
        var badController = new PidController(kp: 20, ki: 0, kd: 0, outputMin: 0, outputMax: 100);
        await RunPhaseAsync(plc, badController, "trace-bad-gains.csv", durationSeconds: 30);

        // === Phase 2: tuned gains -- should settle at the setpoint cleanly. ===
        Console.WriteLine("\n=== Phase 2: TUNED gains -- watch it settle at 50°C ===");
        var goodController = new PidController(kp: 4, ki: 0.4, kd: 3, outputMin: 0, outputMax: 100);
        await RunPhaseAsync(plc, goodController, "trace-good-gains.csv", durationSeconds: 30);

        // === Phase 3: same tuned controller, but disturbed mid-run. ===
        Console.WriteLine("\n=== Phase 3: disturbance rejection -- applying a -10°C kick ===");
        await RunPhaseAsync(plc, goodController, "trace-disturbance.csv", durationSeconds: 20,
            onStart: () => plc.ApplyDisturbanceAsync(10));

        Console.WriteLine("\nTemperature demo done. See trace-*.csv for the raw numbers.");
    }

    private static async Task RunPhaseAsync(
        ITemperatureControlService plc,
        PidController controller,
        string csvFileName,
        int durationSeconds,
        Func<Task>? onStart = null)
    {
        controller.Reset();

        await using var csv = new StreamWriter(csvFileName);
        await csv.WriteLineAsync("TimeSeconds,Setpoint,ProcessValue,Output");

        if (onStart != null)
        {
            await onStart();
        }

        var ticks = (int)(durationSeconds / TickSeconds);
        for (var i = 0; i < ticks; i++)
        {
            var processValue = await plc.ReadProcessValueAsync();
            var output = controller.Compute(Setpoint, processValue, TickSeconds);
            await plc.WriteHeaterPowerAsync(output);

            var timeSeconds = i * TickSeconds;
            Console.WriteLine($"  t={timeSeconds,5:0.0}s  PV={processValue,6:0.00}°C  output={output,6:0.0}%");
            await csv.WriteLineAsync($"{timeSeconds:0.0},{Setpoint:0.0},{processValue:0.00},{output:0.0}");

            await Task.Delay(TickDelay);
        }

        Console.WriteLine($"  (trace written to {csvFileName})");
    }
}
