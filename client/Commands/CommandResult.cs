namespace StirClient.Commands;

/// <summary>
/// Result of sending a command to the PLC. Mirrors SltCommandResult in the
/// real instrument software: the caller gets a clear accepted/rejected
/// answer, not just an exception or a raw status code.
/// </summary>
/// <param name="Accepted">True if the PLC accepted and is acting on the command.</param>
/// <param name="RejectionReason">
/// If the command was rejected, the PLC's status code explaining why
/// (e.g. an out-of-range value). Null when <see cref="Accepted"/> is true.
/// </param>
public record CommandResult(bool Accepted, string? RejectionReason = null);
