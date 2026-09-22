namespace StirClient.Commands;

/// <summary>
/// Command to stir at a given speed. Sent as a write to the PLC's
/// TargetSpeed node.
/// </summary>
/// <param name="Rpm">Target speed in RPM. The PLC rejects values outside 0-1000.</param>
public record StirCommand(double Rpm) : IStirCommand;
