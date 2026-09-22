# OPC UA Stir Demo

A minimal, working example of an OPC UA client talking to a simulated PLC — built to understand the layer that sits between instrument software and hardware in real industrial control systems.

## What's here

- **`server/`** — a mock PLC, built with [node-opcua](https://github.com/node-opcua/node-opcua). It exposes one simulated stir motor:
  - `Stirring.TargetSpeed` (writable) — the command target
  - `Stirring.ActualSpeed` (read/subscribe) — ramps toward the target over time, the way a real motor with inertia would, instead of jumping instantly
  - `Stirring.Running` (read/subscribe)
- **`client/`** — a .NET console app using the official [OPC Foundation .NET client SDK](https://github.com/OPCFoundation/UA-.NETStandard) (`OPCFoundation.NetStandard.Opc.Ua.Client`). It:
  1. Connects to the server over the real OPC UA protocol.
  2. Subscribes to `ActualSpeed` and `Running` — push notifications, not polling.
  3. Sends a command the PLC rejects on purpose (an out-of-range value), to demonstrate a **non-retryable** failure.
  4. Sends a valid "stir at 300 RPM" command and watches the motor ramp up live via the subscription.
  5. Simulates an unreachable instrument (wrong port) to demonstrate a **retryable** failure, with exponential backoff.

## Why it's built this way

This mirrors patterns from real instrument-control software I work adjacent to:

- **Subscriptions over polling** — the client is notified when a value changes, instead of asking on a timer.
- **Two different failure categories, handled differently** — a command the hardware *rejects* (invalid input) is not retried, since retrying won't fix a bad value. A connection that can't be *reached* is treated as transient and retried with backoff, since it might come back.
- **A mapped, typed command shape** — commands carry an explicit target node, a value, and get a clear accept/reject result, rather than being fire-and-forget.

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

## What this is, honestly

I built this as a personal side project to get hands-on with OPC UA client code, after realizing my day-to-day work sits one layer above the PLC-facing code (I work in the business-logic and orchestration layers that talk to instrument software over REST, not the OPC UA client code itself). This project is small and the "PLC" is simulated, but the protocol, the client SDK, and the failure-handling logic are all real.
