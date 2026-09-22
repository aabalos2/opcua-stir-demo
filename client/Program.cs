// A minimal OPC UA client: connects to the mock "PLC" (server/server.js),
// writes a stir command, subscribes to live status the way the real
// SltInstrument.AddSubscriptionsAsync code does, and handles failures the
// same way the real system does -- see SendCommandAsync below.

using Opc.Ua;
using Opc.Ua.Client;

const string ServerUrl = "opc.tcp://localhost:4840/stir-plc";
const string WrongPortUrl = "opc.tcp://localhost:4841/stir-plc"; // used to simulate "instrument unreachable"

var config = BuildAppConfig();
await config.Validate(ApplicationType.Client);
config.CertificateValidator.CertificateValidation += (_, e) => e.Accept = true;

Console.WriteLine($"Connecting to {ServerUrl} ...");
var session = await ConnectAsync(config, ServerUrl);
Console.WriteLine("Connected.\n");

var targetSpeedNode = new NodeId("Stirring.TargetSpeed", 1);
var actualSpeedNode = new NodeId("Stirring.ActualSpeed", 1);
var runningNode = new NodeId("Stirring.Running", 1);

// --- Subscribe to live status: a push notification when the server changes
// the value, the same pattern as SltInstrument.AddSubscriptionsAsync. ---
var subscription = new Subscription(session.DefaultSubscription) { PublishingInterval = 500 };
session.AddSubscription(subscription);
subscription.Create();
AddMonitoredItem(subscription, actualSpeedNode, "ActualSpeed");
AddMonitoredItem(subscription, runningNode, "Running");
subscription.ApplyChanges();

// === 1. A command the PLC itself rejects (like a real system/business error) ===
// Same category as MaybeRetryStepHandler finding a NON-retryable error:
// retrying won't help, the command itself is invalid, so we don't retry it.
Console.WriteLine("=== Sending an invalid command: stir at 1500 RPM (out of range) ===");
await SendCommandAsync(session, targetSpeedNode, 1500.0, "Stir(1500rpm)");

await Task.Delay(TimeSpan.FromSeconds(1));

// === 2. A normal, valid command ===
Console.WriteLine("\n=== Sending a valid command: stir at 300 RPM ===");
await SendCommandAsync(session, targetSpeedNode, 300.0, "Stir(300rpm)");

await Task.Delay(TimeSpan.FromSeconds(5)); // watch it ramp up via the subscription

Console.WriteLine("\n=== Sending stop command ===");
await SendCommandAsync(session, targetSpeedNode, 0.0, "Stop");
await Task.Delay(TimeSpan.FromSeconds(3));

session.Close();

// === 3. The instrument is unreachable entirely (wrong port = nothing there) ===
// Same category as WatchDogInstrumentUnresponsiveError: this is transient
// from the caller's point of view, so it's worth retrying with backoff,
// unlike the rejected command above.
Console.WriteLine("\n=== Simulating an unreachable instrument (retries with backoff) ===");
await ConnectWithRetryAsync(config, WrongPortUrl, maxAttempts: 3);

Console.WriteLine("\nDone.");
return;

// ----------------------------------------------------------------------

static ApplicationConfiguration BuildAppConfig() => new()
{
    ApplicationName = "StirClient",
    ApplicationType = ApplicationType.Client,
    ApplicationUri = "urn:localhost:StirClient",
    SecurityConfiguration = new SecurityConfiguration
    {
        ApplicationCertificate = new CertificateIdentifier
        {
            StoreType = CertificateStoreType.Directory,
            StorePath = "pki/own",
            SubjectName = "CN=StirClient, C=US, S=CA, O=Practice",
        },
        AutoAcceptUntrustedCertificates = true,
        AddAppCertToTrustedStore = true,
        RejectUnknownRevocationStatus = false,
        TrustedPeerCertificates = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = "pki/trusted" },
        TrustedIssuerCertificates = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = "pki/issuer" },
        RejectedCertificateStore = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = "pki/rejected" },
    },
    TransportQuotas = new TransportQuotas { OperationTimeout = 5000 },
    ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
};

static async Task<Session> ConnectAsync(ApplicationConfiguration cfg, string url)
{
    var endpointDescription = CoreClientUtils.SelectEndpoint(cfg, url, useSecurity: false);
    var endpointConfiguration = EndpointConfiguration.Create(cfg);
    var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);
    return await Session.Create(cfg, endpoint, false, "StirClient Session", 60000, null, null);
}

static void AddMonitoredItem(Subscription subscription, NodeId nodeId, string label)
{
    var item = new MonitoredItem(subscription.DefaultItem)
    {
        StartNodeId = nodeId,
        AttributeId = Attributes.Value,
        DisplayName = label,
    };
    item.Notification += (monitoredItem, _) =>
    {
        foreach (var value in monitoredItem.DequeueValues())
        {
            Console.WriteLine($"  [subscription] {label} = {value.Value}");
        }
    };
    subscription.AddItem(item);
}

/// <summary>
/// Sends one command, no retry -- the PLC either accepts or rejects it
/// synchronously. A rejection here is a business/validation failure, the
/// same category MaybeRetryStepHandler treats as non-retryable.
/// </summary>
static async Task SendCommandAsync(Session session, NodeId node, double value, string label)
{
    var writeValue = new WriteValue
    {
        NodeId = node,
        AttributeId = Attributes.Value,
        Value = new DataValue(new Variant(value)),
    };
    session.Write(null, [writeValue], out var results, out _);
    await Task.CompletedTask;

    var status = results[0];
    if (StatusCode.IsBad(status))
    {
        Console.WriteLine($"[{label}] REJECTED by PLC, not retried: {status}");
        return;
    }

    Console.WriteLine($"[{label}] accepted.");
}

/// <summary>
/// Same shape as the watchdog's "instrument unresponsive" handling:
/// a connection failure is treated as transient and retried with
/// exponential backoff, up to a limit, unlike a rejected command.
/// </summary>
static async Task ConnectWithRetryAsync(ApplicationConfiguration cfg, string url, int maxAttempts)
{
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            using var session = await ConnectAsync(cfg, url);
            Console.WriteLine("Connected unexpectedly -- nothing should be listening here.");
            session.Close();
            return;
        }
        catch (Exception ex)
        {
            if (attempt == maxAttempts)
            {
                Console.WriteLine($"[attempt {attempt}/{maxAttempts}] instrument still unresponsive, giving up: {ex.Message}");
                return;
            }

            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 2s, 4s, 8s...
            Console.WriteLine($"[attempt {attempt}/{maxAttempts}] instrument unresponsive: {ex.Message}");
            Console.WriteLine($"  retrying in {delay.TotalSeconds:0}s...");
            await Task.Delay(delay);
        }
    }
}
