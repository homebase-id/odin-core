using System;
using System.IO;
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
    internal static async Task ExportAsync(IServiceProvider services, string domain, string filePath)
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();
        var registry = services.GetRequiredService<IIdentityRegistry>();

        if (File.Exists(filePath))
        {
            logger.LogError("Refusing to overwrite existing file: {path}", filePath);
            return;
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
            return;
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
    }

    internal static async Task ImportAsync(IServiceProvider services, string filePath, bool commit)
    {
        var logger = services.GetRequiredService<ILogger<CommandLine>>();

        if (!File.Exists(filePath))
        {
            logger.LogError("Export file not found: {path}", filePath);
            return;
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

        var config = services.GetRequiredService<OdinConfiguration>();
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
    }
}
