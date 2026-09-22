using Opc.Ua;

namespace StirClient.Services;

/// <summary>
/// Builds the OPC UA client's ApplicationConfiguration. Pulled out on its
/// own so Program.cs doesn't have to know about certificate store details --
/// same reasoning as SltInstrumentFactory keeping construction details out
/// of the code that just wants to use an instrument.
/// </summary>
public static class AppConfigurationFactory
{
    /// <summary>
    /// Builds and validates an ApplicationConfiguration for the client,
    /// with local directory-based certificate stores and auto-accept of
    /// untrusted certificates -- fine for this practice project, not for
    /// a production client talking to a real PLC.
    /// </summary>
    public static async Task<ApplicationConfiguration> CreateAsync()
    {
        var config = new ApplicationConfiguration
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

        await config.Validate(ApplicationType.Client);
        config.CertificateValidator.CertificateValidation += (_, e) => e.Accept = true;

        return config;
    }
}
