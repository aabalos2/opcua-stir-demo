namespace StirClient.Control;

/// <summary>
/// A standard PID controller: computes a control output from the gap
/// between a setpoint and a measured process value.
///
/// Deliberately has no OPC UA/network code in it -- it just takes numbers
/// in and returns a number out, so it's easy to reason about (and to unit
/// test) independent of anything hardware-related.
/// </summary>
/// <param name="kp">Proportional gain -- reacts to the current error.</param>
/// <param name="ki">Integral gain -- reacts to accumulated error over time; eliminates steady-state error.</param>
/// <param name="kd">Derivative gain -- reacts to how fast the error is changing; damps overshoot.</param>
/// <param name="outputMin">Lower clamp on the output (e.g. an actuator can't go below 0% power).</param>
/// <param name="outputMax">Upper clamp on the output (e.g. an actuator can't exceed 100% power).</param>
public class PidController(double kp, double ki, double kd, double outputMin, double outputMax)
{
    private double _integral;
    private double? _previousError;

    /// <summary>
    /// Runs one control-loop tick: given where we want to be (setpoint) and
    /// where we actually are (processValue), returns the next control
    /// output. Must be called on a roughly consistent cadence -- the
    /// integral and derivative terms both depend on <paramref name="deltaTimeSeconds"/>
    /// being accurate.
    /// </summary>
    public double Compute(double setpoint, double processValue, double deltaTimeSeconds)
    {
        var error = setpoint - processValue;

        var proportional = kp * error;

        _integral += error * deltaTimeSeconds;
        var integral = ki * _integral;

        // No previous error yet on the very first tick -- treat rate of
        // change as zero rather than divide by an arbitrary first delta.
        var derivative = _previousError is null
            ? 0
            : kd * (error - _previousError.Value) / deltaTimeSeconds;

        _previousError = error;

        var output = proportional + integral + derivative;
        return Math.Clamp(output, outputMin, outputMax);
    }

    /// <summary>
    /// Clears accumulated integral and derivative history. Call this
    /// between separate demo runs (e.g. switching from bad gains to good
    /// gains) so the old run's accumulated error doesn't bleed into the new one.
    /// </summary>
    public void Reset()
    {
        _integral = 0;
        _previousError = null;
    }
}
