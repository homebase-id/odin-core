using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

// Writes one identity's tables to a single JSON file.
//
// Streams throughout: rows go straight from the reader to the Utf8JsonWriter, so
// memory is flat in the number of rows.
//
// The export runs inside one RepeatableRead transaction per database so all tables
// come from a single snapshot. Without the explicit isolation level the default is
// IsolationLevel.Unspecified, which on Postgres means READ COMMITTED and a fresh
// snapshot per statement.
//
// This class cannot verify the identity is frozen: that needs IIdentityRegistry from
// Odin.Services, which this layer must not reference. The caller asserts it.
public static class IdentityJsonExporter
{
    public static async Task<long> ExportAsync(
        ILogger logger,
        Stream output,
        Guid identityId,
        string domain,
        SystemDatabase systemDatabase,
        IdentityDatabase identityDatabase,
        long identitySchemaVersion,
        long systemSchemaVersion,
        bool callerHasFrozenIdentity)
    {
        if (!callerHasFrozenIdentity)
        {
            throw new InvalidOperationException(
                "Refusing to export: the identity must be frozen first. Disabling an identity "
                + "only closes the HTTP front door; its background workers keep writing.");
        }

        await using var systemTx = await systemDatabase.BeginStackedTransactionAsync(IsolationLevel.RepeatableRead);
        await using var identityTx = await identityDatabase.BeginStackedTransactionAsync(IsolationLevel.RepeatableRead);

        var header = new ExportHeader
        {
            FormatVersion = IdentityExportFile.CurrentFormatVersion,
            ExportedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            IdentityId = identityId,
            Domain = domain,
            IdentitySchemaVersion = identitySchemaVersion,
            SystemSchemaVersion = systemSchemaVersion,
            TableVersions = new Dictionary<string, Dictionary<string, long>>
            {
                [IdentityExportFile.DbSystem] = await systemDatabase.GetTableVersionsAsync(),
                [IdentityExportFile.DbIdentity] = await identityDatabase.GetTableVersionsAsync(),
            },
        };

        await using var writer = new Utf8JsonWriter(output);
        writer.WriteStartArray();
        JsonSerializer.Serialize(writer, header, OdinSystemSerializer.JsonSerializerOptions);

        var rowCount = 0L;

        Task WriteRow(string db, string table, object record)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", IdentityExportFile.KindRow);
            writer.WriteString("db", db);
            writer.WriteString("table", table);
            writer.WritePropertyName("data");
            JsonSerializer.Serialize(writer, record, record.GetType(), OdinSystemSerializer.JsonSerializerOptions);
            writer.WriteEndObject();
            rowCount++;
            return Task.CompletedTask;
        }

        // System rows first, so a truncated file fails on the registration rather than
        // leaving orphaned identity data.
        await systemDatabase.ExportAsync(new OdinId(domain), identityId,
            (table, record) => WriteRow(IdentityExportFile.DbSystem, table, record));

        await identityDatabase.ExportAsync(identityId,
            (table, record) => WriteRow(IdentityExportFile.DbIdentity, table, record));

        writer.WriteEndArray();
        await writer.FlushAsync();

        logger.LogInformation("Exported {count} rows for {domain}", rowCount, domain);
        return rowCount;
    }
}
