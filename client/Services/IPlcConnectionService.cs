using StirClient.Commands;

namespace StirClient.Services;

/// <summary>
/// Service for connecting to the stir motor's PLC over OPC UA and issuing
/// commands to it. Mirrors ISltOpcUaService in the real instrument software.
/// </summary>
public interface IPlcConnectionService : IAsyncDisposable
{
    /// <summary>
    /// Connects to the PLC's OPC UA server and subscribes to its status
    /// nodes (ActualSpeed, Running). Status changes are reported through
    /// <see cref="StatusChanged"/>, not polling.
    /// </summary>
    Task ConnectAsync(string serverUrl);

    /// <summary>
    /// Raised whenever the PLC pushes a status update through the
    /// subscription -- e.g. "ActualSpeed = 150".
    /// </summary>
    event Action<string, object>? StatusChanged;

    /// <summary>
    /// Sends a command to the PLC. A rejected command (bad value) is
    /// reported back as a failed CommandResult, not an exception --
    /// the caller decides whether that's something to retry.
    /// </summary>
    Task<CommandResult> ExecuteCommandAsync(IStirCommand command);
}
