using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

// Writes one identity's tables to a single JSON file.
//
// Streams throughout: rows go straight from the reader to the Utf8JsonWriter, and
// serializing each row's data flushes the writer to the stream, so memory is flat in the
// number of rows.
//
// The export runs inside one RepeatableRead transaction per database so all tables
// come from a single snapshot. Without the explicit isolation level the default is
// IsolationLevel.Unspecified, which on Postgres means READ COMMITTED and a fresh
// snapshot per statement.
//
// This class cannot verify that nothing is writing to the identity: it has no view of
// the hosts that might be. The caller asserts it: the identity was paused (or disabled)
// long enough ago that every node has stopped its workers and jobs and requests that were
// in flight have finished (TenantStatusRules.WhyExportMustWait).
public static class IdentityJsonExporter
{
    // Writes the export to filePath, owner-only, and only once it is complete: it goes to
    // filePath + ".partial" first and is renamed on success, so a failed or interrupted
    // export never leaves a file that looks finished. A failed export deletes its partial
    // file. One left behind by a crash is refused rather than overwritten or deleted: it
    // holds key material, so the operator should see it and delete it.
    public static async Task<long> ExportToFileAsync(
        string filePath,
        Guid identityId,
        string domain,
        SystemDatabase systemDatabase,
        IdentityDatabase identityDatabase,
        long identitySchemaVersion,
        long systemSchemaVersion,
        bool callerCheckedIdentityIsStill,
        ExportPayloadSource? payloadSource = null,
        RowRewriter? rewriteRow = null,
        IReadOnlySet<string>? leaveOutTables = null)
    {
        var partialPath = filePath + ".partial";
        if (File.Exists(filePath))
        {
            throw new IOException($"Refusing to overwrite existing file: {filePath}");
        }

        if (File.Exists(partialPath))
        {
            throw new IOException(
                $"{partialPath} is left over from an export that did not finish. It holds key material: "
                + "delete it, then export again.");
        }

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

        // Outside the try: if this throws, the partial file is not ours to delete
        var stream = new FileStream(partialPath, streamOptions);
        try
        {
            long rows;
            await using (stream)
            {
                rows = await ExportAsync(stream, identityId, domain, systemDatabase, identityDatabase,
                    identitySchemaVersion, systemSchemaVersion, callerCheckedIdentityIsStill, payloadSource, rewriteRow, leaveOutTables);
            }

            File.Move(partialPath, filePath, overwrite: false);
            return rows;
        }
        catch
        {
            File.Delete(partialPath);
            throw;
        }
    }

    public static async Task<long> ExportAsync(
        Stream output,
        Guid identityId,
        string domain,
        SystemDatabase systemDatabase,
        IdentityDatabase identityDatabase,
        long identitySchemaVersion,
        long systemSchemaVersion,
        bool callerCheckedIdentityIsStill,
        ExportPayloadSource? payloadSource = null,
        RowRewriter? rewriteRow = null,
        IReadOnlySet<string>? leaveOutTables = null)
    {
        if (!callerCheckedIdentityIsStill)
        {
            throw new InvalidOperationException(
                "Refusing to export: nothing may be writing to this identity. Pause it and wait "
                + "until it has settled before exporting.");
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
            PayloadSource = payloadSource,
            TableVersions = new Dictionary<string, Dictionary<string, long>>
            {
                [IdentityExportFile.DbSystem] = await systemDatabase.GetTableVersionsAsync(),
                [IdentityExportFile.DbIdentity] = await identityDatabase.GetTableVersionsAsync(),
            },
        };

        await using var writer = new Utf8JsonWriter(output);
        writer.WriteStartArray();
        OdinSystemSerializer.Serialize(writer, header);

        var rowCount = 0L;

        Task WriteRow(string db, string table, object record)
        {
            if (leaveOutTables?.Contains(table) == true)
            {
                return Task.CompletedTask;
            }

            record = rewriteRow?.Invoke(db, table, record) ?? record;
            writer.WriteStartObject();
            writer.WriteString("kind", IdentityExportFile.KindRow);
            writer.WriteString("db", db);
            writer.WriteString("table", table);
            writer.WritePropertyName("data");
            OdinSystemSerializer.Serialize(writer, record, record.GetType());
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
        return rowCount;
    }
}
