using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.System;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

public class ImportResult
{
    public ExportHeader Header { get; init; } = new();
    public long RowsImported { get; set; }
    public Dictionary<string, long> SkippedRowsByTable { get; } = new();
}

// Reads an identity export file and replays it into an empty target.
//
// The export is unconditional; this is where the decision about what to replay lives.
// The default is to import, so a table added to the generator flows through untouched.
public static class IdentityJsonImporter
{
    // Transient state that describes the SOURCE system's in-flight work rather than the
    // identity, and that ranges from useless to actively broken on the target.
    //
    //   Inbox  - rows reference staged files in the inbox folder, which are temp state and
    //            out of scope. Importing them guarantees "File does not exist <inbox key>".
    //   Nonce  - short-lived auth nonces; none is still valid by import time.
    //   Outbox - rows reference long-term files that ARE exported, so replay is structurally
    //            sound once payloads land. Skipped because we cannot verify payloads are
    //            present, nor whether the source is still live and also sending.
    public static readonly IReadOnlySet<string> DefaultSkippedTables =
        new HashSet<string> { "Inbox", "Outbox", "Nonce" };

    private static readonly IReadOnlySet<string> QueueTables = new HashSet<string> { "Inbox", "Outbox" };

    public static async Task<ImportResult> ImportAsync(
        ILogger logger,
        Stream input,
        SystemDatabase targetSystemDatabase,
        IdentityDatabase targetIdentityDatabase,
        bool commit,
        IReadOnlySet<string>? skipTables = null,
        Func<Task>? beforeCommit = null)
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

            await ImportRowsAsync(logger, enumerator, targetSystemDatabase, targetIdentityDatabase, skip, result);

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

            switch (db)
            {
                case IdentityExportFile.DbIdentity:
                    result.RowsImported += await targetIdentityDatabase.ImportRowAsync(
                        table, Deserialize(IdentityDatabase.ExportableRecordTypes, table, data));
                    break;

                case IdentityExportFile.DbSystem:
                    result.RowsImported += await targetSystemDatabase.ImportRowAsync(
                        table, Deserialize(SystemDatabase.ExportableRecordTypes, table, data));
                    break;

                default:
                    throw new IdentityImportRefusedException($"Unknown db discriminator '{db}' for table {table}.");
            }
        }

        foreach (var (table, count) in result.SkippedRowsByTable.OrderBy(kv => kv.Key))
        {
            if (QueueTables.Contains(table))
            {
                // Messages still waiting to be received or sent: they do not arrive on the target
                logger.LogWarning("  skipped {table}: {count} queued item(s), which do not move with the identity", table, count);
            }
            else
            {
                logger.LogInformation("  skipped {table}: {count} row(s)", table, count);
            }
        }
    }

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
