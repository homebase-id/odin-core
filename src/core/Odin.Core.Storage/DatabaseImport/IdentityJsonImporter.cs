using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Storage.Database.System;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

public class ImportResult
{
    public ExportHeader Header { get; init; } = new();
    public long RowsImported { get; set; }
    public Dictionary<string, long> SkippedRowsByTable { get; } = new();

    /// <summary>Queued Inbox and Outbox items that came along, by table.</summary>
    public Dictionary<string, long> CarriedQueueItemsByTable { get; } = new();

    /// <summary>Rows a <see cref="RowFilter"/> left out, by table and reason.</summary>
    public Dictionary<(string table, string reason), long> LeftOutRows { get; } = new();
}

// Reads an identity export file and replays it into an empty target.
//
// The export is unconditional; this is where the decision about what to replay lives.
// The default is to import, so a table added to the generator flows through untouched.
public static class IdentityJsonImporter
{
    // Nonce rows are short-lived auth nonces; none is still valid by import time.
    //
    // The Inbox and Outbox queues move with the identity (#1871): messages received but not yet
    // processed, and messages not yet sent. Their payloads come with the payload move, which fetches
    // the queued items' payloads before anything else; the source was paused before the export, so it
    // is not sending them too. An Inbox item from before #1568, whose files are in the source's inbox
    // folder, cannot come along; the CLI leaves those out with a RowFilter.
    //
    // The export can leave tables out too (leaveOutTables): the CLI leaves DkimKeys out (IdentityKeyMaterial).
    public static readonly IReadOnlySet<string> DefaultSkippedTables = new HashSet<string> { "Nonce" };

    public static async Task<ImportResult> ImportAsync(
        ILogger logger,
        Stream input,
        SystemDatabase targetSystemDatabase,
        IdentityDatabase targetIdentityDatabase,
        bool commit,
        IReadOnlySet<string>? skipTables = null,
        Func<Task>? beforeCommit = null,
        RowRewriter? rewriteRow = null,
        RowFilter? leaveOutRow = null)
    {
        var skip = skipTables ?? DefaultSkippedTables;

        await using var enumerator = OpenElements(input);
        var header = await ReadHeaderAsync(enumerator);

        // Nothing is written until every precondition holds.
        var violations = await IdentityImportPreconditions.CheckAsync(
            header, targetSystemDatabase, targetIdentityDatabase);

        if (violations.Count > 0)
        {
            throw new IdentityImportRefusedException(
                $"Refusing to import {header.Domain}. {violations.Count} precondition(s) failed:"
                + Environment.NewLine + string.Join(Environment.NewLine, violations.Select(v => "  - " + v)));
        }

        var result = new ImportResult { Header = header };

        // The identity rows commit first and the system rows (the registration) last, so the
        // registration is the commit point: if the identity commit fails, the registration never
        // lands, and a rerun clears what did. Commit() only marks a transaction; it commits when
        // disposed, which is why the identity transaction is disposed before the system one is marked.
        await using var systemTransaction = await targetSystemDatabase.BeginStackedTransactionAsync();
        await using (var identityTransaction = await targetIdentityDatabase.BeginStackedTransactionAsync())
        {
            var leftovers = await targetIdentityDatabase.DeleteRowsForIdentityAsync(header.IdentityId);
            if (leftovers > 0)
            {
                logger.LogWarning("Cleared {count} row(s) {domain} had left in the identity tables without a registration",
                    leftovers, header.Domain);
            }

            await ImportRowsAsync(logger, enumerator, targetSystemDatabase, targetIdentityDatabase, skip, rewriteRow,
                leaveOutRow, result);

            if (beforeCommit != null)
            {
                await beforeCommit();
            }

            if (!commit)
            {
                logger.LogInformation("Dry run: rolling back {count} rows for {domain}", result.RowsImported, header.Domain);
                return result;
            }

            identityTransaction.Commit();
        }

        systemTransaction.Commit();
        logger.LogInformation("Imported {count} rows for {domain}", result.RowsImported, header.Domain);
        return result;
    }

    private static async Task ImportRowsAsync(
        ILogger logger,
        IAsyncEnumerator<JsonElement> enumerator,
        SystemDatabase targetSystemDatabase,
        IdentityDatabase targetIdentityDatabase,
        IReadOnlySet<string> skip,
        RowRewriter? rewriteRow,
        RowFilter? leaveOutRow,
        ImportResult result)
    {
        while (await MoveNextAsync(enumerator))
        {
            var element = enumerator.Current;
            var table = StringProperty(element, "table")
                ?? throw new IdentityImportRefusedException("Row is missing its table name.");
            var db = StringProperty(element, "db")
                ?? throw new IdentityImportRefusedException($"Row for {table} is missing its db discriminator.");
            if (!element.TryGetProperty("data", out var data))
            {
                throw new IdentityImportRefusedException($"Row for {table} is missing its data.");
            }

            if (skip.Contains(table))
            {
                result.SkippedRowsByTable.TryGetValue(table, out var soFar);
                result.SkippedRowsByTable[table] = soFar + 1;
                continue;
            }

            var record = db switch
            {
                IdentityExportFile.DbIdentity => Deserialize(IdentityDatabase.ExportableRecordTypes, table, data),
                IdentityExportFile.DbSystem => Deserialize(SystemDatabase.ExportableRecordTypes, table, data),
                _ => throw new IdentityImportRefusedException($"Unknown db discriminator '{db}' for table {table}.")
            };

            if (leaveOutRow?.Invoke(db, table, record) is { } reason)
            {
                result.LeftOutRows.TryGetValue((table, reason), out var leftSoFar);
                result.LeftOutRows[(table, reason)] = leftSoFar + 1;
                continue;
            }

            record = ReleaseQueueClaim(rewriteRow?.Invoke(db, table, record) ?? record);
            result.RowsImported += db == IdentityExportFile.DbIdentity
                ? await targetIdentityDatabase.ImportRowAsync(table, record)
                : await targetSystemDatabase.ImportRowAsync(table, record);

            if (record is InboxRecord or OutboxRecord)
            {
                result.CarriedQueueItemsByTable.TryGetValue(table, out var carriedSoFar);
                result.CarriedQueueItemsByTable[table] = carriedSoFar + 1;
            }
        }

        foreach (var (table, count) in result.CarriedQueueItemsByTable.OrderBy(kv => kv.Key))
        {
            logger.LogInformation("  carried {table}: {count} queued item(s)", table, count);
        }

        foreach (var ((table, reason), count) in result.LeftOutRows.OrderBy(kv => kv.Key.table))
        {
            logger.LogWarning("  left {table}: {count} row(s) behind: {reason}", table, count, reason);
        }

        foreach (var (table, count) in result.SkippedRowsByTable.OrderBy(kv => kv.Key))
        {
            logger.LogInformation("  skipped {table}: {count} row(s)", table, count);
        }
    }

    // A queued item the source had claimed for processing or sending when it was paused is free again
    // here: no node of the target holds that claim. Its retry state (checkOutCount, nextRunTime) stays.
    private static object ReleaseQueueClaim(object record) => record switch
    {
        InboxRecord inbox => inbox with { popStamp = null },
        OutboxRecord outbox => outbox with { checkOutStamp = null },
        _ => record
    };

    // Reads only the header, the file's first element: the rows after it are not parsed, and
    // the reader holds no more of the file than that element.
    public static async Task<ExportHeader> ReadHeaderAsync(Stream input)
    {
        await using var enumerator = OpenElements(input);
        return await ReadHeaderAsync(enumerator);
    }

    // Streamed, not whole-document: DriveMainIndex carries hdrFileMetaData and hdrAppData
    // for every file the identity owns, so a real export does not fit comfortably in
    // memory. The file is a top-level array, which is exactly what
    // DeserializeAsyncEnumerable consumes.
    private static IAsyncEnumerator<JsonElement> OpenElements(Stream input)
    {
        return JsonSerializer
            .DeserializeAsyncEnumerable<JsonElement>(input, OdinSystemSerializer.JsonSerializerOptions)
            .GetAsyncEnumerator();
    }

    private static async Task<ExportHeader> ReadHeaderAsync(IAsyncEnumerator<JsonElement> elements)
    {
        if (!await MoveNextAsync(elements))
        {
            throw new IdentityImportRefusedException("Export file is empty.");
        }

        var header = DeserializeOrRefuse<ExportHeader>(elements.Current, "the header")
            ?? throw new IdentityImportRefusedException("Export file has no readable header.");

        if (header.Kind != IdentityExportFile.KindHeader)
        {
            throw new IdentityImportRefusedException(
                $"Expected the first element to be a header, found '{header.Kind}'.");
        }

        return header;
    }

    private static object Deserialize(
        IReadOnlyDictionary<string, Type> recordTypes, string table, JsonElement data)
    {
        if (!recordTypes.TryGetValue(table, out var type))
        {
            throw new IdentityImportRefusedException(
                $"Export file contains table '{table}', which this binary does not know about.");
        }

        return DeserializeOrRefuse(data, type, $"a {table} row")
            ?? throw new IdentityImportRefusedException($"Row for table '{table}' deserialized to null.");
    }

    // A file that is not valid JSON, or holds values of the wrong shape, is refused like any other bad file
    private static async Task<bool> MoveNextAsync(IAsyncEnumerator<JsonElement> elements)
    {
        try
        {
            return await elements.MoveNextAsync();
        }
        catch (JsonException e)
        {
            throw new IdentityImportRefusedException($"Export file is not valid JSON: {e.Message}", e);
        }
    }

    private static T? DeserializeOrRefuse<T>(JsonElement element, string what)
    {
        return (T?)DeserializeOrRefuse(element, typeof(T), what);
    }

    private static object? DeserializeOrRefuse(JsonElement element, Type type, string what)
    {
        try
        {
            return element.Deserialize(type, OdinSystemSerializer.JsonSerializerOptions);
        }
        catch (JsonException e)
        {
            throw new IdentityImportRefusedException($"Export file has an unreadable value in {what}: {e.Message}", e);
        }
    }

    private static string? StringProperty(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
