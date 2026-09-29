using System;
using System.IO;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.DatabaseImport;
using Odin.Core.Storage.Factory;
using Odin.Core.Time;
using Odin.Services.Configuration;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Registry;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Cli.Commands;

#nullable enable

public static class IdentityJsonTransfer
{
    // False unless this host keeps payloads on S3.
    //
    // Identity transfer moves database rows only. The payload bytes those rows point at
    // have to move by other means, and the only mechanism planned for that is a copy
    // between S3 buckets. A host on local disk has no way to complete the move, so both
    // verbs refuse rather than land an identity whose file headers point at bytes that
    // were never carried across.
    //
    // This reads configuration, not storage. The flag says where this host reads and
    // writes payloads today. It does not prove that every payload of this identity is in
    // the bucket: a host that ran on disk before the flag was turned on still has its
    // older payloads on disk, and nothing here sees that.
    private static bool PayloadsAreOnS3(ILogger logger, OdinConfiguration config, string verb)
    {
        if (config.S3Payload.Enabled)
        {
            return true;
        }

        logger.LogError(
            "Refusing to {verb}: this host stores payloads on local disk (S3Payload:Enabled "
            + "is false). Identity transfer covers database tables only; payloads move "
            + "separately, and only between S3 buckets. A disk-based host cannot complete "
            + "the move, so the identity would arrive with file headers whose bytes are "
            + "missing.",
            verb);
        return false;
    }

    // True when the export file was written. False means it was refused, and the caller
    // turns that into a non-zero exit code.
    internal static async Task<bool> ExportAsync(IServiceProvider services, string domain, string filePath)
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        var registry = services.GetRequiredService<IIdentityRegistry>();
        var config = services.GetRequiredService<OdinConfiguration>();

        if (!PayloadsAreOnS3(logger, config, "export"))
        {
            return false;
        }

        // The CLI builds its own root container; nothing has populated the registry's trie
        // yet, so GetAsync would return null for every domain. Every other verb that reaches
        // for an identity does this first (CommandLine.LoadTenants). It also creates the
        // tenant scope that GetTenantScope below depends on.
        await registry.LoadRegistrations();

        var registration = await registry.GetAsync(domain);
        if (registration == null)
        {
            logger.LogError("No such identity: {domain}", domain);
            return false;
        }

        // The hosts keep running, so the export needs the identity still instead: paused (or disabled)
        // long enough that every node has stopped its workers and jobs and in-flight requests have finished
        var settle = TenantStatusRules.ExportSettleTime(config.Registry.CatchUpIntervalSeconds);
        var mustWait = TenantStatusRules.WhyExportMustWait(registration.Status, registration.StatusChangedAt, UnixTimeUtc.Now(), settle);
        if (mustWait != null)
        {
            logger.LogError("Refusing to export {domain}: {reason}", domain, mustWait);
            return false;
        }

        logger.LogWarning(
            "The export file contains this identity's password data, private keys, TLS "
            + "certificate private key and DKIM signing keys. Anyone holding it can become "
            + "this identity. Store it encrypted and delete it when the migration is done.");

        var systemDatabase = services.GetRequiredService<SystemDatabase>();
        var systemMigrator = services.GetRequiredService<SystemMigrator>();

        var tenantScope = services.GetRequiredService<IMultiTenantContainer>().GetTenantScope(domain);
        var identityDatabase = tenantScope.Resolve<IdentityDatabase>();
        var identityMigrator = tenantScope.Resolve<IdentityMigrator>();

        try
        {
            var rows = await IdentityJsonExporter.ExportToFileAsync(
                logger, filePath, registration.Id, domain,
                systemDatabase, identityDatabase,
                await identityMigrator.GetCurrentVersionAsync(),
                await systemMigrator.GetCurrentVersionAsync(),
                callerCheckedIdentityIsStill: true);

            logger.LogInformation("Exported {rows} rows for {domain} to {path}", rows, domain, filePath);
        }
        catch (IOException e)
        {
            logger.LogError("Export of {domain} failed, no file written: {error}", domain, e.Message);
            return false;
        }

        return true;
    }

    // True when the import ran, dry or committed. False means it was refused, and the
    // caller turns that into a non-zero exit code.
    internal static async Task<bool> ImportAsync(IServiceProvider services, string filePath, bool commit)
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        var config = services.GetRequiredService<OdinConfiguration>();

        if (!PayloadsAreOnS3(logger, config, "import"))
        {
            return false;
        }

        if (!File.Exists(filePath))
        {
            logger.LogError("Export file not found: {path}", filePath);
            return false;
        }

        // Peek at the header to learn which identity this file is for. The importer
        // re-reads it and re-validates; this read is only to build the right scope.
        ExportHeader header;
        await using (var peek = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            header = await IdentityJsonImporter.ReadHeaderAsync(peek);
        }

        var workContainer = services.GetRequiredService<IMultiTenantContainer>();

        await using var targetScope = workContainer.BeginLifetimeScope(cb =>
        {
            cb.RegisterInstance(new OdinIdentity(header.IdentityId, header.Domain)).SingleInstance();
            if (config.Database.Type == DatabaseType.Postgres)
            {
                cb.AddPgsqlIdentityDatabaseServices(header.IdentityId, config.Database.ConnectionString);
            }
            else
            {
                cb.AddSqliteIdentityDatabaseServices(
                    header.IdentityId,
                    new TenantPathManager(config, header.IdentityId).GetIdentityDatabasePath());
            }
        });

        var targetIdentityDatabase = targetScope.Resolve<IdentityDatabase>();
        var targetSystemDatabase = services.GetRequiredService<SystemDatabase>();

        // A fresh identity has version -1 until its per-identity migrations run. Bring the
        // target to the latest schema before comparing table versions, exactly as
        // Sqlite2Pg.ImportIdentityAsync does.
        await targetScope.Resolve<IdentityMigrator>().MigrateAsync();

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var result = await IdentityJsonImporter.ImportAsync(
            logger, stream, targetSystemDatabase, targetIdentityDatabase, commit);

        logger.LogInformation("Imported {rows} rows for {domain} (commit: {commit})",
            result.RowsImported, result.Header.Domain, commit);

        return true;
    }
}
