#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Odin.Hosting;
using Autofac;
using Autofac.Builder;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Hosting.Authentication.Peer;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Background;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// One in-process Odin server. Boots <see cref="Program.CreateHostBuilder"/> over
/// <c>Microsoft.AspNetCore.TestHost.TestServer</c> instead of Kestrel — no ports, no TLS,
/// no real cert handshakes. Hosts one or more tenants via <c>Development:PreconfiguredDomains</c>.
/// </summary>
/// <remarks>
/// <para>
/// Split across three partial-class files: this one carries boot/dispose/config and the env
/// baseline; <see cref="OdinHost"/>.Snapshots.cs carries the per-test reset machinery;
/// <see cref="OdinHost"/>.TestSync.cs carries the <see cref="ITestSync"/> resolver.
/// </para>
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
public sealed partial class OdinHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly List<DbSnapshot> _snapshots = [];

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

        // Holder is populated AFTER host.StartAsync (TestServer not available before that), but
        // resolved lazily by the test peer factory delegate at first request — so this works as
        // long as no peer call fires during host startup.
        var serverHolder = new TestServerHolder();

        var builder = Program.CreateHostBuilder([])
            .ConfigureAppConfiguration(cb => cb.AddInMemoryCollection(overrides))
            // Match production: SystemServices.cs sets AllowSynchronousIO=true on Kestrel for the
            // upload/payload streaming pipeline. TestServer's default rejects sync IO, so without
            // this flag 7 V2 tests fail (reactions + large writes). We're matching production's
            // own flag, not papering over a hidden producer.
            .ConfigureWebHost(web => web.UseTestServer(o => o.AllowSynchronousIO = true))
            .ConfigureServices(services =>
            {
                // Swap the production PeerCapiAuthenticationHandler for the test-side handler on
                // all three peer schemes. The test handler reads X-Test-Peer-Identity and skips
                // the mTLS / session-validate-callback dance, which can't run over TestServer.
                services.AddTransient<TestPeerCapiAuthenticationHandler>();
                services.PostConfigure<AuthenticationOptions>(opts =>
                {
                    foreach (var scheme in opts.Schemes)
                    {
                        if (scheme.Name == PeerAuthConstants.TransitCapiAuthScheme
                            || scheme.Name == PeerAuthConstants.PublicTransitAuthScheme
                            || scheme.Name == PeerAuthConstants.FeedAuthScheme)
                        {
                            scheme.HandlerType = typeof(TestPeerCapiAuthenticationHandler);
                        }
                    }
                });

                // Background services are wired into DI but never StartAsync'd here, so the real
                // IBackgroundServiceManager.NotifyWorkAvailableAsync spins 30s and then throws
                // "Background service not found" — which 500s peer-facing paths (e.g. receiving a
                // connection request enqueues a push notification through the PeerOutbox). The manager
                // lives in the per-tenant container, so it can't be overridden from the root scope;
                // wrap the identity registry's tenant-builder delegate to decorate it per tenant.
                services.Replace(ServiceDescriptor.Singleton<IIdentityRegistry>(sp =>
                    new FileSystemIdentityRegistry(
                        sp.GetRequiredService<ILogger<FileSystemIdentityRegistry>>(),
                        sp.GetRequiredService<ICertificateService>(),
                        sp.GetRequiredService<IDynamicHttpClientFactory>(),
                        sp.GetRequiredService<ISystemHttpClient>(),
                        sp.GetRequiredService<IMultiTenantContainer>(),
                        (cb, registration, cfg) =>
                        {
                            TenantServices.ConfigureTenantServices(cb, registration, cfg);
                            cb.RegisterDecorator<NonNotifyingBackgroundServiceManager, IBackgroundServiceManager>();
                            return cb;
                        },
                        sp.GetRequiredService<OdinConfiguration>())));
            })
            .ConfigureContainer<ContainerBuilder>(cb =>
            {
                cb.RegisterInstance(serverHolder).SingleInstance();
                cb.Register(c => new TestPeerHttpClientFactory(serverHolder, c.Resolve<OdinIdentity>()))
                    .As<IOdinHttpClientFactory>()
                    .InstancePerLifetimeScope();

                // Test-only services. Registered at root; tenant scopes resolve via parent fallback.
                cb.RegisterType<TestSync>()
                    .As<ITestSync>()
                    .InstancePerLifetimeScope();
            });

        var host = builder.Build();
        host.BeforeApplicationStarting([]);
        await host.StartAsync();
        serverHolder.Server = host.GetTestServer();

        return new OdinHost(host, identities, dataRoot);
    }

    /// <summary>
    /// Builds an <see cref="HttpClient"/> bound to this host's <see cref="TestServer"/>.
    /// Caller sets <c>BaseAddress</c> to <c>https://{identity}/</c> so the multi-tenant
    /// middleware resolves the right tenant from the Host header.
    /// </summary>
    public HttpClient CreateClient() => Server.CreateClient();

    /// <summary>
    /// Resolves the Autofac tenant scope for the given identity. Lets tests poke tenant-scoped
    /// services directly (e.g. read/clear <c>FirstRunInfo</c> on a specific tenant's identity DB
    /// for preflight-introduction tests). Use sparingly — most tests should go through HTTP.
    /// </summary>
    public ILifetimeScope GetTenantScope(string domain)
    {
        var multitenant = _host.Services.GetRequiredService<Odin.Services.Tenant.Container.IMultiTenantContainer>();
        var scope = multitenant.LookupTenantScope(domain)
            ?? throw new InvalidOperationException($"no tenant scope for {domain}; was the identity preconfigured + materialized?");
        return scope;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            // Best effort: we're tearing down test infrastructure, but log so a stuck StopAsync
            // (e.g. a hung background service) doesn't disappear silently.
            Console.Error.WriteLine($"[OdinHost.DisposeAsync] StopAsync failed: {ex.GetType().Name}: {ex.Message}");
        }
        _host.Dispose();

        Odin.Hosting.Tests.V2.Auth.OwnerLogin.Forget(this);

        try
        {
            if (Directory.Exists(DataRoot))
            {
                Directory.Delete(DataRoot, recursive: true);
            }
        }
        catch (Exception ex)
        {
            // Best effort: OS will eventually clean /tmp. Log to surface file-lock issues that
            // would otherwise be invisible (and slowly fill /tmp under repeated test runs).
            Console.Error.WriteLine($"[OdinHost.DisposeAsync] Directory.Delete({DataRoot}) failed: {ex.GetType().Name}: {ex.Message}");
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

    // Lazy<T> with ExecutionAndPublication guarantees the factory runs to completion before any
    // other thread observes a non-null value. The earlier CAS-on-an-int pattern flipped the flag
    // BEFORE the SetEnvironmentVariable calls ran, so under ParallelScope.Fixtures a second thread
    // could win the second CAS check, return immediately, and reach Program.CreateHostBuilder's
    // eager AppSettings.LoadConfig before the first thread had finished writing env vars.
    private static readonly Lazy<bool> _baseline = new(InitializeBaseline, LazyThreadSafetyMode.ExecutionAndPublication);

    private static void EnsureGlobalEnvBaseline() => _ = _baseline.Value;

    private static bool InitializeBaseline()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "odin-tests-v2-baseline");
        Directory.CreateDirectory(scratch);

        SetRuntimeBaseline();
        SetStorageBaseline();
        SetDevAndRegistryBaseline();
        SetHostPathsBaseline(scratch);
        SetLoggingBaseline(scratch);
        SetBackgroundServicesBaseline();
        SetCertRenewalBaseline();
        SetMailBaseline();
        SetAdminBaseline();
        SetCdnBaseline();
        return true;
    }

    /// <summary>
    /// Enables the CDN routes and seeds the bearer token used by <c>CdnSession</c>. The token has
    /// to be set before host bootstrap because <c>CdnAuthenticationHandler</c> reads it from
    /// config at registration time. The base URL is a known placeholder — the in-process test
    /// pipeline never resolves it.
    /// </summary>
    private static void SetCdnBaseline()
    {
        Set("Cdn__Enabled", "true");
        Set("Cdn__PayloadBaseUrl", "https://cdn.ravenhosting.cloud");
        Set("Cdn__RequiredAuthToken",
            Odin.Hosting.Tests._V2.ApiClient.TestCases.CdnTestCase.GetAuthToken64());
    }

    private static void SetRuntimeBaseline()
    {
        Set("ASPNETCORE_ENVIRONMENT", "Development");
    }

    /// <summary>SQLite only, no Redis, no S3 — everything in-process / local.</summary>
    private static void SetStorageBaseline()
    {
        Set("Database__Type", "sqlite");
        Set("Redis__Enabled", "false");
        Set("S3PayloadStorage__Enabled", "false");
    }

    /// <summary>
    /// <c>Development:</c> reads dev certs from disk (read by DevEnvironmentSetup at tenant
    /// registration; never used at the transport layer under TestServer). <c>Registry:</c>
    /// values are required-by-schema even though no real DNS work happens in tests.
    /// </summary>
    private static void SetDevAndRegistryBaseline()
    {
        Set("Development__SslSourcePath", "./https/");
        Set("Development__PreconfiguredDomains", "[]"); // overridden per-host

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
    }

    /// <summary>
    /// Paths default to a shared scratch dir, overridden per-host by
    /// <see cref="BuildPerHostConfig"/>. Sockets are zeros — never bound under TestServer.
    /// </summary>
    private static void SetHostPathsBaseline(string scratch)
    {
        Set("Host__TenantDataRootPath", scratch);
        Set("Host__SystemDataRootPath", scratch);
        Set("Host__IPAddressListenList__0__HttpPort", "0");
        Set("Host__IPAddressListenList__0__HttpsPort", "0");
        Set("Host__IPAddressListenList__0__Ip", "127.0.0.1");
        Set("Host__SystemProcessApiKey", Guid.Empty.ToString()); // overridden per-host
        Set("Host__IpRateLimitRequestsPerSecond", int.MaxValue.ToString());
        Set("Host__ClientRegistrationThreshold", int.MaxValue.ToString());
        Set("Host__ClientRegistrationWindowThreshold", int.MaxValue.ToString());
    }

    private static void SetLoggingBaseline(string scratch)
    {
        Set("Logging__LogFilePath", scratch); // overridden per-host
        Set("Logging__EnableStatistics", "true");
    }

    /// <summary>Background services are wired in DI but never <c>StartAsync</c>'d — tests drain explicitly.</summary>
    private static void SetBackgroundServicesBaseline()
    {
        Set("BackgroundServices__EnsureCertificateProcessorIntervalSeconds", "100000");
        Set("BackgroundServices__SystemBackgroundServicesEnabled", "false");
        Set("BackgroundServices__TenantBackgroundServicesEnabled", "false");
    }

    /// <summary>Required-by-schema; never exercised under TestServer.</summary>
    private static void SetCertRenewalBaseline()
    {
        Set("CertificateRenewal__NumberOfCertificateValidationTries", "3");
        Set("CertificateRenewal__UseCertificateAuthorityProductionServers", "false");
        Set("CertificateRenewal__CertificateAuthorityAssociatedEmail", "email@nowhere.com");
        Set("CertificateRenewal__CsrCountryName", "US");
        Set("CertificateRenewal__CsrState", "CA");
        Set("CertificateRenewal__CsrLocality", "Berkeley");
        Set("CertificateRenewal__CsrOrganization", "YF");
        Set("CertificateRenewal__CsrOrganizationUnit", "Dev");
    }

    private static void SetMailBaseline()
    {
        Set("Mailgun__ApiKey", "dontcare");
        Set("Mailgun__DefaultFromEmail", "no-reply@odin.earth");
        Set("Mailgun__EmailDomain", "odin.earth");
        Set("Mailgun__Enabled", "false");
    }

    /// <summary>Admin API disabled — otherwise it would bind a Kestrel port.</summary>
    private static void SetAdminBaseline()
    {
        Set("Admin__ApiEnabled", "false");
        Set("Admin__ApiKey", "your-secret-api-key-here");
        Set("Admin__ApiKeyHttpHeaderName", "Odin-Admin-Api-Key");
        Set("Admin__ApiPort", "0");
        Set("Admin__Domain", "admin.dotyou.cloud");
    }

    private static void Set(string key, string value) =>
        Environment.SetEnvironmentVariable(key, value);
}
