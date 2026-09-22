using Opc.Ua;
using Opc.Ua.Client;
using StirClient.Commands;

namespace StirClient.Services;

/// <summary>
/// Default implementation of <see cref="IPlcConnectionService"/>. A switch dispatches each command type to
/// its own handler, which maps it to a PLC write and returns a clear
/// accepted/rejected result.
/// </summary>
public class PlcConnectionService : IPlcConnectionService
{
    private static readonly NodeId TargetSpeedNode = new("Stirring.TargetSpeed", 1);
    private static readonly NodeId ActualSpeedNode = new("Stirring.ActualSpeed", 1);
    private static readonly NodeId RunningNode = new("Stirring.Running", 1);

    private readonly ApplicationConfiguration _appConfig;
    private Session? _session;
    private Subscription? _subscription;

    /// <inheritdoc />
    public event Action<string, object>? StatusChanged;

    /// <summary>
    /// Creates the service. Does not connect -- call <see cref="ConnectAsync"/>
    /// to actually open a session.
    /// </summary>
    /// <param name="appConfig">The OPC UA application configuration to connect with.</param>
    public PlcConnectionService(ApplicationConfiguration appConfig)
    {
        _appConfig = appConfig;
    }

    /// <inheritdoc />
    public async Task ConnectAsync(string serverUrl)
    {
        var endpointDescription = CoreClientUtils.SelectEndpoint(_appConfig, serverUrl, useSecurity: false);
        var endpointConfiguration = EndpointConfiguration.Create(_appConfig);
        var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

        _session = await Session.Create(_appConfig, endpoint, false, "StirClient Session", 60000, null, null);

        _subscription = new Subscription(_session.DefaultSubscription) { PublishingInterval = 500 };
        _session.AddSubscription(_subscription);
        _subscription.Create();

        AddMonitoredItem(ActualSpeedNode, "ActualSpeed");
        AddMonitoredItem(RunningNode, "Running");
        _subscription.ApplyChanges();
    }

    /// <summary>
    /// Subscribes to a single OPC UA node and raises <see cref="StatusChanged"/>
    /// whenever the server pushes a new value for it.
    /// </summary>
    /// <param name="nodeId">The node to watch.</param>
    /// <param name="label">A friendly name used when reporting changes.</param>
    private void AddMonitoredItem(NodeId nodeId, string label)
    {
        var item = new MonitoredItem(_subscription!.DefaultItem)
        {
            StartNodeId = nodeId,
            AttributeId = Attributes.Value,
            DisplayName = label,
        };
        item.Notification += (monitoredItem, _) =>
        {
            foreach (var value in monitoredItem.DequeueValues())
            {
                StatusChanged?.Invoke(label, value.Value);
            }
        };
        _subscription.AddItem(item);
    }

    /// <inheritdoc />
    public Task<CommandResult> ExecuteCommandAsync(IStirCommand command)
    {
        return command switch
        {
            StirCommand stirCommand => ExecuteStirAsync(stirCommand),
            StopCommand => ExecuteStirAsync(new StirCommand(0)),
            _ => throw new ArgumentException($"Unknown command type {command.GetType()}"),
        };
    }

    /// <summary>
    /// Writes the given target speed to the PLC's TargetSpeed node and
    /// reports whether it was accepted.
    /// </summary>
    private Task<CommandResult> ExecuteStirAsync(StirCommand command)
    {
        var writeValue = new WriteValue
        {
            NodeId = TargetSpeedNode,
            AttributeId = Attributes.Value,
            Value = new DataValue(new Variant(command.Rpm)),
        };

        _session!.Write(null, [writeValue], out var results, out _);

        var status = results[0];
        return Task.FromResult(StatusCode.IsBad(status)
            ? new CommandResult(Accepted: false, RejectionReason: status.ToString())
            : new CommandResult(Accepted: true));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _session?.Close();
        _session?.Dispose();
        return ValueTask.CompletedTask;
    }
}
