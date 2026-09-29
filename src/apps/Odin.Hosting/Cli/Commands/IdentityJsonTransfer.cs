using System;
using System.IO;
using System.Linq;
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
using Odin.Services.JobManagement;
using Odin.Services.Registry;
using Odin.Services.Registry.PayloadMove;
using Odin.Services.Tenant.Container;

namespace Odin.Hosting.Cli.Commands;

#nullable enable

public static class IdentityJsonTransfer
{
    // One identity's database services, without loading the registry
    private static ILifetimeScope BeginIdentityScope(IServiceProvider services, OdinConfiguration config, Guid identityId, string domain)
    {
        return services.GetRequiredService<IMultiTenantContainer>().BeginLifetimeScope(cb =>
        {
            cb.RegisterInstance(new OdinIdentity(identityId, domain)).SingleInstance();
            cb.ConfigureDatabaseServices(identityId, config);
        });
    }

    // Where this host serves moved payloads: its provisioning domain, on the public HTTPS port
    private static string PayloadSourceBaseUrl(OdinConfiguration config)
    {
        return new UriBuilder("https", config.Registry.ProvisioningDomain, config.Host.DefaultHttpsPort).Uri
            .GetLeftPart(UriPartial.Authority);
    }

    // True when the export file was written. False means it was refused, and the caller
    // turns that into a non-zero exit code.
    internal static async Task<bool> ExportAsync(IServiceProvider services, string domain, string filePath)
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        var config = services.GetRequiredService<OdinConfiguration>();

        // The file tells the target where to fetch the payloads; never promise what this host will not serve
        if (!config.PayloadMove.SourceEnabled)
        {
            logger.LogError(
                "Refusing to export {domain}: PayloadMove:SourceEnabled is off, so the target could not fetch the "
                + "identity's payloads from this host", domain);
            return false;
        }

        // Straight from the database: loading the registry would load every identity on the host
        // (migrations, version-upgrade checks, caches) to export one
        var systemDatabase = services.GetRequiredService<SystemDatabase>();
        var record = (await systemDatabase.Registrations.GetAllAsync())
            .SingleOrDefault(r => r.primaryDomainName.Equals(domain, StringComparison.OrdinalIgnoreCase));
        if (record == null)
        {
            logger.LogError("No such identity: {domain}", domain);
            return false;
        }

        var registration = new IdentityRegistration { Id = record.identityId, PrimaryDomainName = record.primaryDomainName };
        RegistrationJsonMapper.Apply(registration, record.disabled, record.json);

        // The identity must be still: paused (or disabled) long enough that every node has stopped its
        // workers and jobs and requests that were in flight have finished
        var settle = TenantStatusRules.ExportSettleTime(config.Registry.CatchUpIntervalSeconds);
        var mustWait = TenantStatusRules.WhyExportMustWait(registration.Status, registration.StatusChangedAt, UnixTimeUtc.Now(), settle);
        if (mustWait != null)
        {
            logger.LogError("Refusing to export {domain}: {reason}", domain, mustWait);
            return false;
        }

        logger.LogWarning(
            "The export file contains this identity's password data, private keys and TLS "
            + "certificate private key, in the clear. Anyone holding it can become "
            + "this identity. Store it encrypted and delete it when the migration is done.");

        var systemMigrator = services.GetRequiredService<SystemMigrator>();
        await using var identityScope = BeginIdentityScope(services, config, registration.Id, registration.PrimaryDomainName);
        var identityDatabase = identityScope.Resolve<IdentityDatabase>();
        var identityMigrator = identityScope.Resolve<IdentityMigrator>();

        // Minted before the file exists, so it can go in it. Exporting again replaces it, which revokes
        // whatever an earlier file could redeem.
        var payloadSource = new ExportPayloadSource
        {
            BaseUrl = PayloadSourceBaseUrl(config),
            HandoffToken = await services.GetRequiredService<PayloadMoveSource>().MintHandoffAsync(registration.Id)
        };

        try
        {
            var rows = await IdentityJsonExporter.ExportToFileAsync(
                filePath, registration.Id, registration.PrimaryDomainName,
                systemDatabase, identityDatabase,
                await identityMigrator.GetCurrentVersionAsync(),
                await systemMigrator.GetCurrentVersionAsync(),
                callerCheckedIdentityIsStill: true,
                payloadSource,
                IdentityKeyMaterial.ForExport(config.CertificateRenewal.StorageKey, logger));

            logger.LogInformation("Exported {rows} rows for {domain} to {path}", rows, domain, filePath);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
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

        if (!File.Exists(filePath))
        {
            logger.LogError("Export file not found: {path}", filePath);
            return false;
        }

        // Read the header to learn which identity this file is for, then rewind: the importer
        // reads it again and validates it; this read is only to build the right scope.
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        ExportHeader header;
        try
        {
            header = await IdentityJsonImporter.ReadHeaderAsync(stream);
        }
        catch (IdentityImportRefusedException e)
        {
            logger.LogError("{message}", e.Message);
            return false;
        }
        stream.Position = 0;

        if (config.Database.Type == DatabaseType.Sqlite)
        {
            // A new identity has no folder yet, and SQLite cannot create identity.db without one
            new TenantPathManager(config, header.IdentityId).CreateDirectories();
        }

        await using var targetScope = BeginIdentityScope(services, config, header.IdentityId, header.Domain);

        var targetIdentityDatabase = targetScope.Resolve<IdentityDatabase>();
        var targetSystemDatabase = services.GetRequiredService<SystemDatabase>();

        // A fresh identity has version -1 until its per-identity migrations run. Bring the
        // target to the latest schema before comparing table versions, exactly as
        // Sqlite2Pg.ImportIdentityAsync does.
        await targetScope.Resolve<IdentityMigrator>().MigrateAsync();

        // The system database only if it was never set up (a target host that has not started yet).
        // A running target's schema belongs to its hosts: migrating it here would upgrade it to this
        // binary's version under them, and hide a version mismatch from the preconditions.
        if ((await targetSystemDatabase.GetTableVersionsAsync()).Values.All(version => version == -1))
        {
            logger.LogInformation("The target system database is empty; creating it");
            await targetSystemDatabase.MigrateDatabaseAsync();
        }

        // The identity lands paused whatever status it was exported with, so it serves nothing until
        // DNS points here and the operator resumes it; running hosts load it from the database
        try
        {
            await IdentityJsonImporter.ImportAsync(logger, stream, targetSystemDatabase, targetIdentityDatabase, commit,
                beforeCommit: async () =>
                {
                    await FileSystemIdentityRegistry.MarkImportedRegistrationPausedAsync(targetSystemDatabase, header.IdentityId);

                    // In the same transaction, so the transfer exists if and only if the import committed. It
                    // starts as soon as a running host picks it up, paused or not, and fetches only the files the
                    // import brought: those up to the highest rowId there is now.
                    if (header.PayloadSource is { } payloadSource)
                    {
                        await PayloadMoveJob.ScheduleAsync(services.GetRequiredService<IJobManager>(), header.IdentityId,
                            payloadSource.BaseUrl, payloadSource.HandoffToken,
                            await targetIdentityDatabase.DriveMainIndex.GetMaxRowIdAsync());
                    }
                },
                rewriteRow: IdentityKeyMaterial.ForImport(config.CertificateRenewal.StorageKey));
        }
        catch (IdentityImportRefusedException e)
        {
            // A failed precondition or an unreadable file; nothing was committed
            logger.LogError("{message}", e.Message);
            return false;
        }

        if (header.PayloadSource == null)
        {
            logger.LogWarning("The file names no payload source: {domain}'s payloads will not be moved", header.Domain);
        }
        else if (commit)
        {
            logger.LogInformation("{domain}'s payloads transfer from {source} in the background, starting now; " +
                                  "follow it with odin-admin tenant payload-move", header.Domain, header.PayloadSource.BaseUrl);
        }

        if (commit)
        {
            logger.LogInformation(
                "{domain} is paused. Running hosts load it within {interval} s. Resume it once DNS points here.",
                header.Domain, config.Registry.CatchUpIntervalSeconds);
        }

        return true;
    }
}
