// Composition root -- builds the config and service, then exercises them.
// The connection, subscription, and command-dispatch logic all live in
// Services/PlcConnectionService.cs, not here.

using StirClient;
using StirClient.Commands;
using StirClient.Services;

const string ServerUrl = "opc.tcp://localhost:4840/stir-plc";
const string WrongPortUrl = "opc.tcp://localhost:4841/stir-plc"; // simulates "instrument unreachable"

var appConfig = await AppConfigurationFactory.CreateAsync();

await using var plc = new PlcConnectionService(appConfig);
plc.StatusChanged += (label, value) => Console.WriteLine($"  [subscription] {label} = {value}");

Console.WriteLine($"Connecting to {ServerUrl} ...");
await plc.ConnectAsync(ServerUrl);
Console.WriteLine("Connected.\n");

// === 1. A command the PLC rejects -- not retried, same as MaybeRetryStepHandler
// treating a non-retryable system error: retrying an invalid value won't help. ===
Console.WriteLine("=== Sending an invalid command: stir at 1500 RPM (out of range) ===");
await RunCommandAsync(plc, new StirCommand(1500), "Stir(1500rpm)");
await Task.Delay(TimeSpan.FromSeconds(1));

// === 2. A normal, valid command ===
Console.WriteLine("\n=== Sending a valid command: stir at 300 RPM ===");
await RunCommandAsync(plc, new StirCommand(300), "Stir(300rpm)");
await Task.Delay(TimeSpan.FromSeconds(5)); // watch it ramp up via the subscription

Console.WriteLine("\n=== Sending stop command ===");
await RunCommandAsync(plc, new StopCommand(), "Stop");
await Task.Delay(TimeSpan.FromSeconds(3));

// === 3. The instrument is unreachable entirely -- retried with backoff,
// same shape as WatchDogInstrumentUnresponsiveError handling. ===
Console.WriteLine("\n=== Simulating an unreachable instrument (retries with backoff) ===");
await ConnectWithRetryAsync(appConfig, WrongPortUrl, maxAttempts: 3);

// === 4. The temperature/PID control-loop demo -- a separate plant on the
// same server. See TemperatureDemo.cs. ===
await TemperatureDemo.RunAsync(appConfig, ServerUrl);

Console.WriteLine("\nDone.");
return;

/// <summary>
/// Sends one command and prints whether the PLC accepted or rejected it.
/// A rejection is reported, not retried -- see the "=== 1." block above.
/// </summary>
static async Task RunCommandAsync(IPlcConnectionService plc, IStirCommand command, string label)
{
    var result = await plc.ExecuteCommandAsync(command);
    Console.WriteLine(result.Accepted
        ? $"[{label}] accepted."
        : $"[{label}] REJECTED by PLC, not retried: {result.RejectionReason}");
}

/// <summary>
/// Attempts to connect to an (intentionally) unreachable instrument, retrying
/// with exponential backoff -- the connection-failure counterpart to
/// RunCommandAsync's rejected-command handling: this failure mode IS
/// retried, because the instrument might come back.
/// </summary>
static async Task ConnectWithRetryAsync(Opc.Ua.ApplicationConfiguration appConfig, string url, int maxAttempts)
{
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        await using var plc = new PlcConnectionService(appConfig);
        try
        {
            await plc.ConnectAsync(url);
            Console.WriteLine("Connected unexpectedly -- nothing should be listening here.");
            return;
        }
        catch (Exception ex)
        {
            if (attempt == maxAttempts)
            {
                Console.WriteLine($"[attempt {attempt}/{maxAttempts}] instrument still unresponsive, giving up: {ex.Message}");
                return;
            }

            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 2s, 4s, 8s...
            Console.WriteLine($"[attempt {attempt}/{maxAttempts}] instrument unresponsive: {ex.Message}");
            Console.WriteLine($"  retrying in {delay.TotalSeconds:0}s...");
            await Task.Delay(delay);
        }
    }
}
