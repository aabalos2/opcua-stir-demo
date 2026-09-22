// A tiny mock PLC, exposed over OPC UA.
// It simulates one stir motor: you write a target speed, and the "hardware"
// ramps its actual speed toward that target over time, the way a real motor
// with inertia would -- it doesn't jump instantly.

const { OPCUAServer, Variant, DataType, StatusCodes } = require("node-opcua");

const RAMP_RATE_RPM_PER_TICK = 15; // how fast the "motor" can accelerate
const TICK_MS = 200;

/**
 * Starts the mock PLC: an OPC UA server exposing one stir motor
 * (Stirring.TargetSpeed / ActualSpeed / Running) and a simulation loop
 * that ramps ActualSpeed toward TargetSpeed over time.
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

  await server.start();
  console.log(`Mock PLC OPC UA server running at ${server.getEndpointUrl()}`);
  console.log("Nodes: Stirring.TargetSpeed (writable), Stirring.ActualSpeed, Stirring.Running");
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
