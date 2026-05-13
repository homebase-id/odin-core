#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Storage.Factory;
using Odin.Services.Configuration;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Tests.V2.Hosting;

/// <summary>
/// Per-test reset machinery: snapshot identity DB once at fixture setup, restore before each test.
/// See <c>tests/apps/Odin.Hosting.Tests.V2/README.md</c> for the lifecycle overview.
/// </summary>
public sealed partial class OdinHost
{
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
    /// <c>BackupSqliteDatabase.Execute</c> switches the journal mode via a PRAGMA, which
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
}
