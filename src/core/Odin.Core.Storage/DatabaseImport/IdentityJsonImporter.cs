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

    public static async Task<ImportResult> ImportAsync(
        ILogger logger,
        Stream input,
        SystemDatabase targetSystemDatabase,
        IdentityDatabase targetIdentityDatabase,
        bool commit,
        IReadOnlySet<string>? skipTables = null)
    {
        var skip = skipTables ?? DefaultSkippedTables;

        using var document = await JsonDocument.ParseAsync(input);
        var elements = document.RootElement.EnumerateArray();

        if (!elements.MoveNext())
        {
            throw new InvalidOperationException("Export file is empty.");
        }

        var header = JsonSerializer.Deserialize<ExportHeader>(
            elements.Current.GetRawText(), OdinSystemSerializer.JsonSerializerOptions)
            ?? throw new InvalidOperationException("Export file has no readable header.");

        if (header.Kind != IdentityExportFile.KindHeader)
        {
            throw new InvalidOperationException(
                $"Expected the first element to be a header, found '{header.Kind}'.");
        }

        // Nothing is written until every precondition holds.
        var violations = await IdentityImportPreconditions.CheckAsync(
            header, targetSystemDatabase, targetIdentityDatabase);

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"Refusing to import {header.Domain}. {violations.Count} precondition(s) failed:"
                + Environment.NewLine + string.Join(Environment.NewLine, violations.Select(v => "  - " + v)));
        }

        var result = new ImportResult { Header = header };

        await using var systemTransaction = await targetSystemDatabase.BeginStackedTransactionAsync();
        await using var identityTransaction = await targetIdentityDatabase.BeginStackedTransactionAsync();

        while (elements.MoveNext())
        {
            var element = elements.Current;
            var table = element.GetProperty("table").GetString()
                ?? throw new InvalidOperationException("Row is missing its table name.");
            var db = element.GetProperty("db").GetString()
                ?? throw new InvalidOperationException($"Row for {table} is missing its db discriminator.");
            var data = element.GetProperty("data");

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
                    throw new InvalidOperationException($"Unknown db discriminator '{db}' for table {table}.");
            }
        }

        foreach (var (table, count) in result.SkippedRowsByTable.OrderBy(kv => kv.Key))
        {
            logger.LogInformation("  skipped {table}: {count} row(s)", table, count);
        }

        if (!commit)
        {
            logger.LogInformation("Dry run: rolling back {count} rows for {domain}",
                result.RowsImported, header.Domain);
        }
        else
        {
            logger.LogInformation("Imported {count} rows for {domain}", result.RowsImported, header.Domain);
            systemTransaction.Commit();
            identityTransaction.Commit();
        }

        return result;
    }

    private static object Deserialize(
        IReadOnlyDictionary<string, Type> recordTypes, string table, JsonElement data)
    {
        if (!recordTypes.TryGetValue(table, out var type))
        {
            throw new InvalidOperationException(
                $"Export file contains table '{table}', which this binary does not know about.");
        }

        return JsonSerializer.Deserialize(data.GetRawText(), type, OdinSystemSerializer.JsonSerializerOptions)
            ?? throw new InvalidOperationException($"Row for table '{table}' deserialized to null.");
    }
}
