# OPC UA + PID Control Demo

A working example of an OPC UA client talking to a simulated PLC, plus a real PID temperature control loop layered on top — built to close two gaps at once: the hardware-interface layer I work adjacent to, and classical control theory (PID, feedback stability, tuning).

## What's here

- **`server/`** — a mock PLC, built with [node-opcua](https://github.com/node-opcua/node-opcua). It exposes two simulated systems:
  - **A stir motor:** `Stirring.TargetSpeed` (writable), `Stirring.ActualSpeed` / `Stirring.Running` (read/subscribe) — ramps toward its target over time instead of jumping instantly.
  - **A temperature plant:** `Temperature.HeaterPower` (writable, 0-100%), `Temperature.ProcessValue` (read, noisy), `Temperature.DisturbanceKick` (writable, one-shot). The server only simulates raw physics here — no setpoint, no control logic. Deciding *how much* heater power to apply to reach and hold a target is the PID controller's job, in the client, not the PLC's.

It's a small Node.js program that starts an OPC UA server. In real life, the PLC itself either has a built-in OPC UA server or has one running in front of it. The C# client doesn't know or care that it's talking to a Node script instead of real hardware — it connects over the same OPC UA protocol either way.

- **`client/`** — a .NET console app using the official [OPC Foundation .NET client SDK](https://github.com/OPCFoundation/UA-.NETStandard) (`OPCFoundation.NetStandard.Opc.Ua.Client`). It:
  1. Connects to the server over the real OPC UA protocol.
  2. Subscribes to `ActualSpeed` and `Running` — push notifications, not polling.
  3. Sends a command the PLC rejects on purpose (an out-of-range value), to demonstrate a **non-retryable** failure.
  4. Sends a valid "stir at 300 RPM" command and watches the motor ramp up live via the subscription.
  5. Simulates an unreachable instrument (wrong port) to demonstrate a **retryable** failure, with exponential backoff.
  6. Runs a **PID temperature control loop** (`Control/PidController.cs`, `TemperatureDemo.cs`) against the temperature plant: first with deliberately bad gains, then tuned gains, then a mid-run disturbance — writing each run's trace to a CSV.

## The PID control loop

`Control/PidController.cs` is a standard PID controller with no OPC UA or hardware code in it — just numbers in, a number out, so it's easy to reason about independent of anything else:

```
output = Kp * error + Ki * (running sum of error) + Kd * (rate of change of error)
```

- **P** reacts to the current error. Alone, it settles with a permanent **steady-state error** against any standing disturbance (like constant heat loss to ambient), because as the error shrinks, so does the corrective push.
- **I** reacts to accumulated error over time, which is what actually eliminates that steady-state error — even a small persistent gap keeps adding up until it forces the error to zero.
- **D** reacts to how fast the error is changing, damping overshoot — but it's sensitive to sensor noise, which is why `Temperature.ProcessValue` deliberately reports a noisy reading, not a clean one.

### A real finding: why the first version of the plant couldn't oscillate

The first version of the temperature plant was a single first-order lag (temperature chases one equilibrium value). Running deliberately bad gains (Kp=20, no I or D) against it didn't produce the oscillation I expected — it produced a permanent steady-state error and a jittery *output*, but the actual temperature stayed in a tight band. The reason turned out to be a real, correct control-theory fact: **a single first-order lag under pure proportional control cannot sustain oscillation, no matter how high the gain** — it just converges faster, or with more offset. Real ovens and bioreactors oscillate when mistuned because they're *not* one lump of thermal mass; heat moves from a heater element, through the media, before a sensor ever sees it — a genuine phase lag between "I commanded more heat" and "the process responded."

The fix: I remodeled the plant as **two thermal masses in series** — the heater element, then the process/sensor mass it heats (`HEATER_ELEMENT_LAG_RATE` and `PROCESS_LAG_RATE` in `server.js`). That's what actually makes aggressive gains overshoot: with Kp=20, the process now visibly overshoots the 50°C setpoint (peaking at ~50.3°C) before correcting and settling into a small oscillation band below target — a real, honest reproduction of the failure mode described in the job posting that prompted this project, not a contrived one. Tuned gains (Kp=4, Ki=0.4, Kd=3) converge smoothly with no overshoot, and the same tuned controller recovers cleanly from a mid-run -10°C disturbance. All three runs' real traces are in `client/trace-*.csv`.

### Known limitation, honestly

The controller has **no anti-windup**: during the initial saturated climb (output pinned at 100% for several seconds while temperature is far below setpoint), the integral term accumulates without limit if `Ki` is large enough, which can cause a bigger overshoot than the gains alone would predict. The current tuned gains happen to avoid this being a major issue, but a production controller would clamp or otherwise limit integral accumulation while the output is saturated. Left as a next step, not silently fixed.

## Why it's built this way

This mirrors patterns from real instrument-control software I work adjacent to:

- **Subscriptions over polling** — the client is notified when a value changes, instead of asking on a timer.
- **Two different failure categories, handled differently** — a command the hardware *rejects* (invalid input) is not retried, since retrying won't fix a bad value. A connection that can't be *reached* is treated as transient and retried with backoff, since it might come back. Real code (`RetryOperationService.HandleAsync`) makes the same split: some error types skip backoff entirely and are handled another way, while the rest get a wait that grows with the retry count.
- **A mapped, typed command shape** — commands carry an explicit target node, a value, and get a clear accept/reject result, rather than being fire-and-forget.
- **Control logic lives above the hardware interface, not inside it** — the PLC only exposes raw sensor/actuator I/O (`HeaterPower` in, `ProcessValue` out); the PID controller deciding *how* to drive that I/O lives entirely in the client. That split is deliberate, not incidental — it's the same shape as "own everything from the interface of the cloud software through the low-level control of a physical device."

### Where this demo simplifies the real pattern

- **This demo uses literal exponential backoff** (`2^attempt` seconds). The real retry logic steps through fixed **short / medium / long** buckets based on retry count — graduated, not exponential. Both back off; the math isn't the same.
- **This demo retries with an in-process `Task.Delay` loop.** The real system schedules the retry by publishing to a **RabbitMQ delay exchange** — the wait is held by the message broker, not a sleeping loop in the service.

## Running it

The server has to be running before you start the client. In Rider:

Open a terminal.
Run:
cd server
npm start
Leave that terminal running — you should see:
Mock PLC OPC UA server running at opc.tcp://.../stir-plc
Then run/debug the client in Rider.
