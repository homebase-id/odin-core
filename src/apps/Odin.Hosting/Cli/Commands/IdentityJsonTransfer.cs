using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;
using Odin.Core.Storage.DatabaseImport;
using Odin.Core.Storage.Factory;
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

    // False unless the host looks stopped. Local probe only: it cannot see a host on
    // another machine sharing the same database. See HostLivenessCheck.
    //
    // Export needs this because a running host's tenant background workers keep writing
    // to the identity database and nothing here can stop them, so the snapshot would lose
    // whatever they commit after it. Import needs it because it writes the shared system
    // tables, and a running host would neither see the new identity nor expect its
    // registration to appear.
    private static bool HostIsStopped(ILogger logger, OdinConfiguration config, string verb)
    {
        var listening = HostLivenessCheck.FindListeningPorts(config);
        if (listening.Count == 0)
        {
            return true;
        }

        logger.LogError(
            "Refusing to {verb}: something is listening on port(s) {ports}, so a host is "
            + "still running. Stop it first. A running host's tenant background workers keep "
            + "writing to the identity database and this command cannot stop them.",
            verb, string.Join(", ", listening));
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

        if (!HostIsStopped(logger, config, "export"))
        {
            return false;
        }

        if (File.Exists(filePath))
        {
            logger.LogError("Refusing to overwrite existing file: {path}", filePath);
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

        logger.LogWarning(
            "The export file contains this identity's password data, private keys, TLS "
            + "certificate private key and DKIM signing keys. Anyone holding it can become "
            + "this identity. Store it encrypted and delete it when the migration is done.");

        var systemDatabase = services.GetRequiredService<SystemDatabase>();
        var systemMigrator = services.GetRequiredService<SystemMigrator>();

        var tenantScope = services.GetRequiredService<IMultiTenantContainer>().GetTenantScope(domain);
        var identityDatabase = tenantScope.Resolve<IdentityDatabase>();
        var identityMigrator = tenantScope.Resolve<IdentityMigrator>();

        // Owner-only from the moment the file exists. The file is the identity, and setting
        // the mode after the export would leave it umask-readable (typically 0644) for the
        // whole write, which on a real identity is minutes.
        var streamOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
        };
        if (!OperatingSystem.IsWindows())
        {
            streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using (var stream = new FileStream(filePath, streamOptions))
        {
            // CommandLine already aborted if a host was listening. This process holds no
            // background workers of its own (CommandLine disables them) and cannot stop
            // another host's, so there is nothing to freeze here.
            var rows = await IdentityJsonExporter.ExportAsync(
                logger, stream, registration.Id, domain,
                systemDatabase, identityDatabase,
                await identityMigrator.GetCurrentVersionAsync(),
                await systemMigrator.GetCurrentVersionAsync(),
                callerHasFrozenIdentity: true);

            logger.LogInformation("Exported {rows} rows for {domain} to {path}", rows, domain, filePath);
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

        if (!HostIsStopped(logger, config, "import"))
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
            using var document = await JsonDocument.ParseAsync(peek);
            var first = document.RootElement[0].GetRawText();
            header = OdinSystemSerializer.Deserialize<ExportHeader>(first)
                ?? throw new InvalidOperationException("Export file has no readable header.");
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
