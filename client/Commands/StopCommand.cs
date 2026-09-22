namespace StirClient.Commands;

/// <summary>
/// Command to stop the motor. Equivalent to a stir command at 0 RPM,
/// but modeled as its own command since that's how the real instrument
/// software separates them (StirContinuouslyCommand vs StopStirringCommand).
/// </summary>
public record StopCommand : IStirCommand;
