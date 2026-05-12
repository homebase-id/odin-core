#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Odin.Hosting;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// One in-process Odin server. Boots <see cref="Program.CreateHostBuilder"/> over
/// <c>Microsoft.AspNetCore.TestHost.TestServer</c> instead of Kestrel — no ports, no TLS,
/// no real cert handshakes. Hosts one or more tenants via <c>Development:PreconfiguredDomains</c>.
/// </summary>
/// <remarks>
/// <para>
/// Configuration injection is split in two layers so that multiple hosts can run in the same
/// process (and in parallel) without clobbering each other:
/// </para>
/// <list type="bullet">
///   <item><description>
///     A <b>static, idempotent env-var baseline</b> set once on first <see cref="StartAsync"/>.
///     This satisfies the eager reads in <c>Program.CreateHostBuilder</c> (which calls
///     <c>AppSettings.LoadConfig(true)</c> and <c>Directory.CreateDirectory</c> before
///     returning a builder) using a shared scratch dir. Everything in this layer is process-stable.
///   </description></item>
///   <item><description>
///     <b>Per-host <see cref="IConfigurationBuilder.AddInMemoryCollection"/> overrides</b>
///     stacked on top via a follow-up <c>ConfigureAppConfiguration</c>. These win at DI bind time
///     (in-memory provider is registered last), so each host's services see their own
///     tenant data root, log path, preconfigured domains, and system-process API key.
///   </description></item>
/// </list>
/// </remarks>
public sealed class OdinHost : IAsyncDisposable
{
    private readonly IHost _host;
    public TestServer Server { get; }
    public string[] Identities { get; }
    public string DataRoot { get; }

    private OdinHost(IHost host, string[] identities, string dataRoot)
    {
        _host = host;
        Server = host.GetTestServer();
        Identities = identities;
        DataRoot = dataRoot;
    }

    public static async Task<OdinHost> StartAsync(params string[] identities)
    {
        if (identities.Length == 0)
        {
            throw new ArgumentException("At least one identity is required", nameof(identities));
        }

        var dataRoot = Path.Combine(Path.GetTempPath(), "odin-tests-v2", Guid.NewGuid().ToString("N"));
        var tenantData = Path.Combine(dataRoot, "tenants");
        var systemData = Path.Combine(dataRoot, "system");
        var logs = Path.Combine(dataRoot, "logs");
        Directory.CreateDirectory(tenantData);
        Directory.CreateDirectory(systemData);
        Directory.CreateDirectory(logs);

        EnsureGlobalEnvBaseline();

        var overrides = BuildPerHostConfig(identities, tenantData, systemData, logs);

        var builder = Program.CreateHostBuilder([])
            .ConfigureAppConfiguration(cb => cb.AddInMemoryCollection(overrides))
            .ConfigureWebHost(web => web.UseTestServer());

        var host = builder.Build();
        host.BeforeApplicationStarting([]);
        await host.StartAsync();

        return new OdinHost(host, identities, dataRoot);
    }

    /// <summary>
    /// Builds an <see cref="HttpClient"/> bound to this host's <see cref="TestServer"/>.
    /// Caller sets <c>BaseAddress</c> to <c>https://{identity}/</c> so the multi-tenant
    /// middleware resolves the right tenant from the Host header.
    /// </summary>
    public HttpClient CreateClient() => Server.CreateClient();

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // best effort; we're tearing down test infrastructure
        }
        _host.Dispose();

        try
        {
            if (Directory.Exists(DataRoot))
            {
                Directory.Delete(DataRoot, recursive: true);
            }
        }
        catch
        {
            // best effort; OS will eventually clean /tmp
        }
    }

    private static Dictionary<string, string?> BuildPerHostConfig(
        string[] identities, string tenantData, string systemData, string logs)
    {
        var cfg = new Dictionary<string, string?>
        {
            ["Host:TenantDataRootPath"] = tenantData,
            ["Host:SystemDataRootPath"] = systemData,
            ["Host:SystemProcessApiKey"] = Guid.NewGuid().ToString(),
            ["Logging:LogFilePath"] = logs,
        };

        for (var i = 0; i < identities.Length; i++)
        {
            cfg[$"Development:PreconfiguredDomains:{i}"] = identities[i];
        }

        return cfg;
    }

    // ---------------------------------------------------------------------------------------------
    // One-shot, process-wide env baseline. Idempotent under parallel callers.
    // ---------------------------------------------------------------------------------------------

    private static int _baselineInitialized;

    private static void EnsureGlobalEnvBaseline()
    {
        if (Interlocked.CompareExchange(ref _baselineInitialized, 1, 0) != 0)
        {
            return;
        }

        var scratch = Path.Combine(Path.GetTempPath(), "odin-tests-v2-baseline");
        Directory.CreateDirectory(scratch);

        // Runtime mode
        Set("ASPNETCORE_ENVIRONMENT", "Development");

        // Storage backends (all in-process / local for speed)
        Set("Database__Type", "sqlite");
        Set("Redis__Enabled", "false");
        Set("S3PayloadStorage__Enabled", "false");

        // Dev certs on disk — read by DevEnvironmentSetup during tenant registration.
        // The cert files exist but are never used at the transport layer (no TLS under TestServer).
        Set("Development__SslSourcePath", "./https/");
        Set("Development__PreconfiguredDomains", "[]"); // overridden per-host

        // Registry config (required-by-schema even though no real DNS work happens in tests)
        Set("Registry__ProvisioningDomain", "provisioning.dotyou.cloud");
        Set("Registry__ManagedDomains", "[\"dev.dotyou.cloud\"]");
        Set("Registry__DnsTargetRecordType", "[\"dev.dotyou.cloud\"]");
        Set("Registry__DnsTargetAddress", "[\"dev.dotyou.cloud\"]");
        Set("Registry__DnsRecordValues__ApexARecords", "[\"127.0.0.1\"]");
        Set("Registry__DnsRecordValues__ApexAliasRecord", "provisioning.dotyou.cloud");
        Set("Registry__DnsRecordValues__CApiCnameTarget", "");
        Set("Registry__DnsRecordValues__FileCnameTarget", "");
        Set("Registry__AutomatedIdentityKey", "e36f5077-bec3-4410-89fe-5bc822dc4c8d");
        Set("Registry__AutomatedPasswordRecoveryIdentities", "[]");

        // Host paths & sockets — paths are baseline scratch, overridden per-host; sockets unused under TestServer.
        Set("Host__TenantDataRootPath", scratch);
        Set("Host__SystemDataRootPath", scratch);
        Set("Host__IPAddressListenList__0__HttpPort", "0");
        Set("Host__IPAddressListenList__0__HttpsPort", "0");
        Set("Host__IPAddressListenList__0__Ip", "127.0.0.1");
        Set("Host__SystemProcessApiKey", Guid.Empty.ToString()); // overridden per-host
        Set("Host__IpRateLimitRequestsPerSecond", int.MaxValue.ToString());
        Set("Host__ClientRegistrationThreshold", int.MaxValue.ToString());
        Set("Host__ClientRegistrationWindowThreshold", int.MaxValue.ToString());

        // Logging — baseline path overridden per-host
        Set("Logging__LogFilePath", scratch);
        Set("Logging__EnableStatistics", "true");

        // Background services off — tests drive everything explicitly
        Set("BackgroundServices__EnsureCertificateProcessorIntervalSeconds", "100000");
        Set("BackgroundServices__SystemBackgroundServicesEnabled", "false");
        Set("BackgroundServices__TenantBackgroundServicesEnabled", "false");

        // Cert renewal — required-by-schema; never exercised under TestServer
        Set("CertificateRenewal__NumberOfCertificateValidationTries", "3");
        Set("CertificateRenewal__UseCertificateAuthorityProductionServers", "false");
        Set("CertificateRenewal__CertificateAuthorityAssociatedEmail", "email@nowhere.com");
        Set("CertificateRenewal__CsrCountryName", "US");
        Set("CertificateRenewal__CsrState", "CA");
        Set("CertificateRenewal__CsrLocality", "Berkeley");
        Set("CertificateRenewal__CsrOrganization", "YF");
        Set("CertificateRenewal__CsrOrganizationUnit", "Dev");

        // Mail (disabled)
        Set("Mailgun__ApiKey", "dontcare");
        Set("Mailgun__DefaultFromEmail", "no-reply@odin.earth");
        Set("Mailgun__EmailDomain", "odin.earth");
        Set("Mailgun__Enabled", "false");

        // Admin API off (would otherwise bind a Kestrel port)
        Set("Admin__ApiEnabled", "false");
        Set("Admin__ApiKey", "your-secret-api-key-here");
        Set("Admin__ApiKeyHttpHeaderName", "Odin-Admin-Api-Key");
        Set("Admin__ApiPort", "0");
        Set("Admin__Domain", "admin.dotyou.cloud");
    }

    private static void Set(string key, string value) =>
        Environment.SetEnvironmentVariable(key, value);
}
