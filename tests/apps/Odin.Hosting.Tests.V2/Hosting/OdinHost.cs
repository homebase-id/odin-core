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
using Autofac;
using Odin.Core.Storage.Factory;
using Odin.Core.Identity;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Background.Testing;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Tests.V2.Hosting;

// TODO: split into partial class files when this file passes ~450 LOC — natural seams are
//   (1) boot/dispose/config, (2) snapshot/reset, (3) test-sync resolve, (4) EnsureGlobalEnvBaseline.
// The env-baseline block bundles unrelated subsystems (storage, registry, certs, mail, admin) for
// brevity; consider per-subsystem helpers if a baseline value breaks something and you have to
// scroll a wall of Set() calls to find it.

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
            .ConfigureWebHost(web => web.UseTestServer(o => o.AllowSynchronousIO = true))
            .ConfigureContainer<ContainerBuilder>(cb =>
            {
                cb.RegisterInstance(serverHolder).SingleInstance();
                cb.Register(c => new TestPeerHttpClientFactory(serverHolder, c.Resolve<OdinIdentity>()))
                    .As<IOdinHttpClientFactory>()
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

        Odin.Hosting.Tests.V2.Auth.OwnerLogin.Forget(this);

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

    // ---------------------------------------------------------------------------------------------
    // Per-test reset machinery: snapshot identity DB once at fixture setup, restore before each test.
    // See Plan: per-test SQLite snapshot/restore.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Force lazy tenant scope creation for each preconfigured identity by hitting an anonymous V2
    /// endpoint. After this returns the per-tenant identity DB file is guaranteed to exist on disk.
    /// </summary>
    public async Task EnsureTenantsMaterializedAsync()
    {
        using var client = Server.CreateClient();
        foreach (var identity in Identities)
        {
            var resp = await client.GetAsync($"https://{identity}/api/v2/health/ping");
            resp.EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    /// Capture each materialized tenant's identity DB to a sibling <c>.snap</c> file. Subsequent
    /// <see cref="ResetAsync"/> calls restore from these snapshots.
    /// </summary>
    /// <remarks>
    /// Clears each tenant's connection pool before snapshotting —
    /// <see cref="BackupSqliteDatabase.Execute"/> switches the journal mode via a PRAGMA, which
    /// fails with "database is locked" if any pooled connection from the warm-up still has the
    /// file open. We don't dispose the tenant scope itself: the multi-tenant middleware looks the
    /// scope up via <c>GetTenantScope</c> (which throws if absent) rather than recreating it, so
    /// disposing the scope would 500 every subsequent request.
    /// </remarks>
    public async Task TakeBaselineAsync()
    {
        _snapshots.Clear();
        var config = _host.Services.GetRequiredService<OdinConfiguration>();
        var registry = _host.Services.GetRequiredService<IIdentityRegistry>();
        var multitenant = _host.Services.GetRequiredService<IMultiTenantContainer>();

        foreach (var identity in Identities)
        {
            var registration = registry.ResolveIdentityRegistration(identity, out _);
            var paths = new TenantPathManager(config, registration.Id);
            var dbPath = paths.GetIdentityDatabasePath();
            if (!File.Exists(dbPath))
            {
                throw new InvalidOperationException(
                    $"Identity DB not found at {dbPath} — did EnsureTenantsMaterializedAsync run?");
            }

            await ClearTenantConnectionPoolAsync(multitenant, identity);

            var snapshot = new DbSnapshot(identity, dbPath);
            await snapshot.TakeAsync();
            _snapshots.Add(snapshot);
        }
    }

    /// <summary>
    /// Reset every snapshotted tenant to its baseline: drain the tenant's connection pool (so the
    /// DB file is no longer held open), copy the snapshot back over the live identity DB, and wipe
    /// the non-DB tenant directories (payloads / temp / inbox). The tenant scope stays alive — the
    /// next request resolves a fresh connection from the now-empty pool against the restored file.
    /// </summary>
    public async Task ResetAsync()
    {
        if (_snapshots.Count == 0)
        {
            return;
        }

        var config = _host.Services.GetRequiredService<OdinConfiguration>();
        var registry = _host.Services.GetRequiredService<IIdentityRegistry>();
        var multitenant = _host.Services.GetRequiredService<IMultiTenantContainer>();

        foreach (var snapshot in _snapshots)
        {
            await ClearTenantConnectionPoolAsync(multitenant, snapshot.Domain);
            await snapshot.RestoreAsync();

            var registration = registry.ResolveIdentityRegistration(snapshot.Domain, out _);
            var paths = new TenantPathManager(config, registration.Id);
            ResetDirectory(paths.PayloadsPath);
            ResetDirectory(paths.UploadPath);
            ResetDirectory(paths.InboxPath);
        }
    }

    private static async Task ClearTenantConnectionPoolAsync(IMultiTenantContainer multitenant, string domain)
    {
        var scope = multitenant.LookupTenantScope(domain);
        if (scope == null)
        {
            return;
        }

        var pool = scope.Resolve<IDbConnectionPool>();
        await pool.ClearAllAsync();
    }

    private static void ResetDirectory(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        Directory.CreateDirectory(path);
    }

    // ---------------------------------------------------------------------------------------------
    // Test-only synchronous hooks (peer outbox / inbox drains). See ITestSync.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves <see cref="ITestSync"/> from <paramref name="domain"/>'s tenant scope. Available
    /// because <c>Testing__EnableSyncHooks</c> is set in the global env baseline; in production
    /// the binding doesn't exist and this would throw.
    /// </summary>
    public ITestSync GetTestSync(string domain)
    {
        var multitenant = _host.Services.GetRequiredService<IMultiTenantContainer>();
        var scope = multitenant.LookupTenantScope(domain)
            ?? throw new InvalidOperationException(
                $"No tenant scope for {domain} — call EnsureTenantsMaterializedAsync first.");
        return scope.Resolve<ITestSync>();
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

        SetRuntimeBaseline();
        SetStorageBaseline();
        SetDevAndRegistryBaseline();
        SetHostPathsBaseline(scratch);
        SetLoggingBaseline(scratch);
        SetBackgroundServicesBaseline();
        SetTestingBaseline();
        SetCertRenewalBaseline();
        SetMailBaseline();
        SetAdminBaseline();
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

    /// <summary>Enables the tenant-scope ITestSync registration; gates the peer-auth test bypass.</summary>
    private static void SetTestingBaseline()
    {
        Set("Testing__EnableSyncHooks", "true");
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
