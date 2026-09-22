using Opc.Ua;
using Opc.Ua.Client;

namespace StirClient.Services;

/// <inheritdoc cref="ITemperatureControlService"/>
public class TemperatureControlService : ITemperatureControlService
{
    private static readonly NodeId ProcessValueNode = new("Temperature.ProcessValue", 1);
    private static readonly NodeId HeaterPowerNode = new("Temperature.HeaterPower", 1);
    private static readonly NodeId DisturbanceKickNode = new("Temperature.DisturbanceKick", 1);

    private readonly ApplicationConfiguration _appConfig;
    private Session? _session;

    public TemperatureControlService(ApplicationConfiguration appConfig)
    {
        _appConfig = appConfig;
    }

    /// <inheritdoc />
    public async Task ConnectAsync(string serverUrl)
    {
        var endpointDescription = CoreClientUtils.SelectEndpoint(_appConfig, serverUrl, useSecurity: false);
        var endpointConfiguration = EndpointConfiguration.Create(_appConfig);
        var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);
        _session = await Session.Create(_appConfig, endpoint, false, "TemperatureControl Session", 60000, null, null);
    }

    /// <inheritdoc />
    public Task<double> ReadProcessValueAsync() => ReadDoubleAsync(ProcessValueNode);

    /// <inheritdoc />
    public Task WriteHeaterPowerAsync(double percent) => WriteDoubleAsync(HeaterPowerNode, percent);

    /// <inheritdoc />
    public Task ApplyDisturbanceAsync(double degrees) => WriteDoubleAsync(DisturbanceKickNode, degrees);

    private Task<double> ReadDoubleAsync(NodeId nodeId)
    {
        var value = _session!.ReadValue(nodeId);
        return Task.FromResult(Convert.ToDouble(value.Value));
    }

    private Task WriteDoubleAsync(NodeId nodeId, double value)
    {
        var writeValue = new WriteValue
        {
            NodeId = nodeId,
            AttributeId = Attributes.Value,
            Value = new DataValue(new Variant(value)),
        };
        _session!.Write(null, [writeValue], out _, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _session?.Close();
        _session?.Dispose();
        return ValueTask.CompletedTask;
    }
}
