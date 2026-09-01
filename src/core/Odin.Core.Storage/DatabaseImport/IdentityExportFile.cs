using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

#nullable enable

namespace Odin.Core.Storage.DatabaseImport;

// Shape of the identity export file. A single JSON array whose first element is the
// header and whose remaining elements are one row each.
//
// An array rather than {"tables":{...}} so the file streams in both directions with
// stock APIs: Utf8JsonWriter out, JsonSerializer.DeserializeAsyncEnumerable in.
// DriveMainIndex carries hdrFileMetaData and hdrAppData for every file the identity
// owns, so whole-document parsing is not safe to assume.
public static class IdentityExportFile
{
    // Describes the envelope only: header fields and row shape. Independent of the
    // per-table schema versions, which live in the header's TableVersions.
    public const int CurrentFormatVersion = 1;

    public const string KindHeader = "header";
    public const string KindRow = "row";

    public const string DbIdentity = "identity";
    public const string DbSystem = "system";
}

public class ExportHeader
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = IdentityExportFile.KindHeader;
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; }
    [JsonPropertyName("exportedAt")] public long ExportedAt { get; set; }
    [JsonPropertyName("identityId")] public Guid IdentityId { get; set; }
    [JsonPropertyName("domain")] public string Domain { get; set; } = "";
    [JsonPropertyName("identitySchemaVersion")] public long IdentitySchemaVersion { get; set; }
    [JsonPropertyName("systemSchemaVersion")] public long SystemSchemaVersion { get; set; }

    // db name -> table name -> per-table schema version. Authoritative for the
    // all-or-nothing compatibility check on import.
    [JsonPropertyName("tableVersions")]
    public Dictionary<string, Dictionary<string, long>> TableVersions { get; set; } = new();
}

public class ExportRow
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = IdentityExportFile.KindRow;
    [JsonPropertyName("db")] public string Db { get; set; } = "";
    [JsonPropertyName("table")] public string Table { get; set; } = "";
    [JsonPropertyName("data")] public JsonElement Data { get; set; }
}
