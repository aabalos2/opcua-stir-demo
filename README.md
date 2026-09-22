# OPC UA Stir Demo

A minimal, working example of an OPC UA client talking to a simulated PLC — built to understand the layer that sits between instrument software and hardware in real industrial control systems.

## What's here

- **`server/`** — a mock PLC, built with [node-opcua](https://github.com/node-opcua/node-opcua). It exposes one simulated stir motor:
  - `Stirring.TargetSpeed` (writable) — the command target
  - `Stirring.ActualSpeed` (read/subscribe) — ramps toward the target over time, the way a real motor with inertia would, instead of jumping instantly
  - `Stirring.Running` (read/subscribe)

It's a small Node.js program that starts an OPC UA server. In real life, the PLC itself either has a built-in OPC UA server or has one running in front of it. The C# client doesn't know or care that it's talking to a Node script instead of real hardware — it connects over the same OPC UA protocol either way.


- **`client/`** — a .NET console app using the official [OPC Foundation .NET client SDK](https://github.com/OPCFoundation/UA-.NETStandard) (`OPCFoundation.NetStandard.Opc.Ua.Client`). It:
  1. Connects to the server over the real OPC UA protocol.
  2. Subscribes to `ActualSpeed` and `Running` — push notifications, not polling.
  3. Sends a command the PLC rejects on purpose (an out-of-range value), to demonstrate a **non-retryable** failure.
  4. Sends a valid "stir at 300 RPM" command and watches the motor ramp up live via the subscription.
  5. Simulates an unreachable instrument (wrong port) to demonstrate a **retryable** failure, with exponential backoff.

## Why it's built this way

This mirrors patterns from real instrument-control software I work adjacent to:

- **Subscriptions over polling** — the client is notified when a value changes, instead of asking on a timer.
- **Two different failure categories, handled differently** — a command the hardware *rejects* (invalid input) is not retried, since retrying won't fix a bad value. A connection that can't be *reached* is treated as transient and retried with backoff, since it might come back. Real code (`RetryOperationService.HandleAsync`) makes the same split: some error types skip backoff entirely and are handled another way, while the rest get a wait that grows with the retry count.
- **A mapped, typed command shape** — commands carry an explicit target node, a value, and get a clear accept/reject result, rather than being fire-and-forget.

### Where this demo simplifies the real pattern

- **This demo uses literal exponential backoff** (`2^attempt` seconds). The real retry logic steps through fixed **short / medium / long** buckets based on retry count — graduated, not exponential. Both back off; the math isn't the same.
- **This demo retries with an in-process `Task.Delay` loop.** The real system schedules the retry by publishing to a **RabbitMQ delay exchange** — the wait is held by the message broker, not a sleeping loop in the service.

## Running it

```bash
# terminal 1
cd server
npm install
npm start

# terminal 2
cd client
dotnet run
```
