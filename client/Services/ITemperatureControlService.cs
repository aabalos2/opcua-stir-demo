namespace StirClient.Services;

/// <summary>
/// Service for reading the simulated temperature sensor and driving the
/// simulated heater over OPC UA. Deliberately simple -- unlike
/// IPlcConnectionService's subscription-based status, a control loop reads
/// on its own fixed timer, since PID math depends on a consistent time
/// step between reads, not "whenever the server happens to push a change."
/// </summary>
public interface ITemperatureControlService : IAsyncDisposable
{
    /// <summary>Connects to the PLC's OPC UA server.</summary>
    Task ConnectAsync(string serverUrl);

    /// <summary>Reads the current (noisy) temperature sensor value.</summary>
    Task<double> ReadProcessValueAsync();

    /// <summary>Writes the heater power command, 0-100.</summary>
    Task WriteHeaterPowerAsync(double percent);

    /// <summary>
    /// Applies a one-shot disturbance, immediately dropping the true
    /// temperature by this many degrees -- simulates something like a door
    /// opening. Used to demonstrate disturbance rejection.
    /// </summary>
    Task ApplyDisturbanceAsync(double degrees);
}
