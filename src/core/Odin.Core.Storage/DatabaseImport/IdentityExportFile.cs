using System;
using System.Collections.Generic;
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
    // 2: the header names where the payloads can be fetched (payloadSource), which an older binary would
    // ignore and so import the identity without them.
    // 3: the certificate's private key is in the clear, so a host with another storage key can re-encrypt it
    // under its own; a version 2 file has it encrypted under the source's key. Older files are refused.
    public const int CurrentFormatVersion = 3;

    public const string KindHeader = "header";
    public const string KindRow = "row";

    public const string DbIdentity = "identity";
    public const string DbSystem = "system";
}

/// <summary>
/// Lets the caller change a row on its way into or out of the file: key material kept under a host's own key is
/// re-keyed here, by the host layer that owns those keys. Returns the row to write or insert.
/// </summary>
public delegate object RowRewriter(string db, string table, object record);

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

    // Where the target fetches the identity's payloads, and the single-use token it redeems there.
    // Null in an export that carries no payload source.
    [JsonPropertyName("payloadSource")] public ExportPayloadSource? PayloadSource { get; set; }
}

public class ExportPayloadSource
{
    [JsonPropertyName("baseUrl")] public string BaseUrl { get; set; } = "";
    [JsonPropertyName("handoffToken")] public string HandoffToken { get; set; } = "";
}
