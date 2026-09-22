// A tiny mock PLC, exposed over OPC UA.
// It simulates one stir motor: you write a target speed, and the "hardware"
// ramps its actual speed toward that target over time, the way a real motor
// with inertia would -- it doesn't jump instantly.

const { OPCUAServer, Variant, DataType, StatusCodes } = require("node-opcua");

const RAMP_RATE_RPM_PER_TICK = 15; // how fast the "motor" can accelerate
const TICK_MS = 200;

// --- Temperature plant constants ---
// Modeled as TWO thermal masses in series -- the heater element, then the
// process/media it's heating (which is also where the sensor sits) -- not
// one lump. This matters: a single first-order lag mathematically CANNOT
// sustain oscillation under pure proportional control, no matter how high
// the gain. Two lags in series (a real phase lag between "I commanded more
// heat" and "the process actually got hotter") is what lets aggressive
// gains genuinely overshoot and oscillate, the way a real oven or
// bioreactor does when mistuned.
const AMBIENT_TEMP_C = 22; // room temperature -- what temperature decays toward with no heater power
const HEATER_GAIN = 0.6; // °C of eventual heater-element heating per 1% of heater power
const HEATER_ELEMENT_LAG_RATE = 0.15; // the heater element itself responds relatively quickly
const PROCESS_LAG_RATE = 0.03; // the process/sensor mass responds slower -- this is the source of real phase lag
const SENSOR_NOISE_C = 0.3; // +/- random noise added to the reported (not true) temperature

/**
 * Starts the mock PLC: an OPC UA server exposing one stir motor
 * (Stirring.TargetSpeed / ActualSpeed / Running) and a temperature plant
 * (Temperature.HeaterPower / ProcessValue / DisturbanceKick), each with its
 * own simulation loop modeling real inertia.
 */
async function main() {
  const server = new OPCUAServer({
    port: 4840,
    resourcePath: "/stir-plc",
    buildInfo: {
      productName: "MockStirPLC",
      buildNumber: "1",
      buildDate: new Date(),
    },
  });

  await server.initialize();

  const addressSpace = server.engine.addressSpace;
  const namespace = addressSpace.getOwnNamespace();

  const stirring = namespace.addObject({
    organizedBy: addressSpace.rootFolder.objects,
    browseName: "Stirring",
  });

  let targetSpeed = 0;
  let actualSpeed = 0;
  let running = false;

  // Writable node -- this is what a client command ("stir at 300 RPM") writes to.
  namespace.addVariable({
    componentOf: stirring,
    browseName: "TargetSpeed",
    nodeId: "s=Stirring.TargetSpeed",
    dataType: "Double",
    value: {
      // Called when a client reads TargetSpeed.
      get: () => new Variant({ dataType: DataType.Double, value: targetSpeed }),
      // Called when a client writes TargetSpeed -- this is the PLC's own
      // validation, run before the value is accepted. A real PLC would
      // reject an out-of-range command like this the same way.
      set: (variant) => {
        const value = variant.value;
        if (value < 0 || value > 1000) {
          return StatusCodes.BadOutOfRange;
        }
        targetSpeed = value;
        running = value > 0;
        console.log(`[PLC] TargetSpeed command received: ${value} RPM`);
        return StatusCodes.Good;
      },
    },
  });

  // Read-only node -- a client SUBSCRIBES to this to get live status,
  // the same pattern as the real SltInstrument.AddSubscriptionsAsync code.
  namespace.addVariable({
    componentOf: stirring,
    browseName: "ActualSpeed",
    nodeId: "s=Stirring.ActualSpeed",
    dataType: "Double",
    value: {
      // No setter -- clients can't write this directly, only the
      // simulation loop below updates it, the same way a real motor's
      // actual speed isn't something you can just assign.
      get: () => new Variant({ dataType: DataType.Double, value: actualSpeed }),
    },
  });

  namespace.addVariable({
    componentOf: stirring,
    browseName: "Running",
    nodeId: "s=Stirring.Running",
    dataType: "Boolean",
    value: {
      get: () => new Variant({ dataType: DataType.Boolean, value: running }),
    },
  });

  // The "hardware" simulation loop -- ramps actualSpeed toward targetSpeed.
  setInterval(() => {
    if (actualSpeed < targetSpeed) {
      actualSpeed = Math.min(targetSpeed, actualSpeed + RAMP_RATE_RPM_PER_TICK);
    } else if (actualSpeed > targetSpeed) {
      actualSpeed = Math.max(targetSpeed, actualSpeed - RAMP_RATE_RPM_PER_TICK);
    }
    running = actualSpeed > 0;
  }, TICK_MS);

  // --- Temperature plant: raw physics only. No setpoint and no control
  // logic live here -- this node only knows "how much heater power am I
  // being told to apply" and "what does the sensor read." Deciding *how
  // much* power to apply to reach and hold a target is the PID
  // controller's job, in the client. ---
  const temperature = namespace.addObject({
    organizedBy: addressSpace.rootFolder.objects,
    browseName: "Temperature",
  });

  let heaterPower = 0; // 0-100, the actuator command written by the client
  let heaterElementTemp = AMBIENT_TEMP_C; // the heater element's own temperature (hidden -- not directly sensed)
  let trueTemp = AMBIENT_TEMP_C; // the real process temperature (hidden -- ProcessValue reports a noisy version of this)

  // Writable -- this is the PID controller's output, applied to the "heater."
  namespace.addVariable({
    componentOf: temperature,
    browseName: "HeaterPower",
    nodeId: "s=Temperature.HeaterPower",
    dataType: "Double",
    value: {
      get: () => new Variant({ dataType: DataType.Double, value: heaterPower }),
      set: (variant) => {
        const value = variant.value;
        if (value < 0 || value > 100) {
          return StatusCodes.BadOutOfRange;
        }
        heaterPower = value;
        return StatusCodes.Good;
      },
    },
  });

  // Read-only, and deliberately noisy -- a client subscribes to this the
  // same way it subscribes to ActualSpeed. Real sensors are never
  // perfectly clean, which is why a raw derivative term is noise-sensitive.
  namespace.addVariable({
    componentOf: temperature,
    browseName: "ProcessValue",
    nodeId: "s=Temperature.ProcessValue",
    dataType: "Double",
    value: {
      get: () => new Variant({
        dataType: DataType.Double,
        value: trueTemp + (Math.random() * 2 - 1) * SENSOR_NOISE_C,
      }),
    },
  });

  // Writable, one-shot -- write a positive number to immediately knock the
  // true temperature down by that much, simulating something like a door
  // opening and cold air rushing in. Used to demonstrate disturbance
  // rejection: does the controller recover cleanly, or does it overshoot
  // and oscillate on the way back?
  namespace.addVariable({
    componentOf: temperature,
    browseName: "DisturbanceKick",
    nodeId: "s=Temperature.DisturbanceKick",
    dataType: "Double",
    value: {
      get: () => new Variant({ dataType: DataType.Double, value: 0 }),
      set: (variant) => {
        const value = variant.value;
        trueTemp -= value;
        console.log(`[PLC] Disturbance applied: -${value}°C`);
        return StatusCodes.Good;
      },
    },
  });

  // The thermal simulation loop -- this is the "physics," modeled as two
  // lags in series:
  //   1. Heater power pushes the heater ELEMENT's own temperature toward
  //      an equilibrium (fairly quickly -- it's a small mass).
  //   2. The process temperature then chases the heater element's
  //      temperature, not the heater power directly (slower -- it's a
  //      bigger mass). That's the real phase lag between "I commanded
  //      more heat" and "the process actually got hotter," and it's what
  //      makes oscillation under aggressive gains physically possible.
  // With zero heater power, everything decays back to ambient -- a
  // standing disturbance a P-only controller can never fully overcome,
  // which is exactly why integral action exists.
  setInterval(() => {
    const heaterEquilibrium = AMBIENT_TEMP_C + heaterPower * HEATER_GAIN;
    heaterElementTemp += (heaterEquilibrium - heaterElementTemp) * HEATER_ELEMENT_LAG_RATE;
    trueTemp += (heaterElementTemp - trueTemp) * PROCESS_LAG_RATE;
  }, TICK_MS);

  await server.start();
  console.log(`Mock PLC OPC UA server running at ${server.getEndpointUrl()}`);
  console.log("Nodes: Stirring.TargetSpeed (writable), Stirring.ActualSpeed, Stirring.Running");
  console.log("Nodes: Temperature.HeaterPower (writable), Temperature.ProcessValue, Temperature.DisturbanceKick (writable)");
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
