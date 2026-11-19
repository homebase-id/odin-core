using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Apps;

public class ClientFileMetadata
{
    public Guid? GlobalTransitId { get; set; }

    public UnixTimeUtc Created { get; set; }

    public UnixTimeUtc Updated { get; set; }

    /// <summary>
    /// When the file got its <see cref="VersionTag"/>: the <see cref="Updated"/> of the write that gave it, which writes
    /// that keep the tag (reactions, transfer history, local app data, shares) don't move.  V2 only; absent for a file
    /// that has none, such as the tombstone of a file no longer shared with the caller.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UnixTimeUtc? VersionUpdated { get; set; }

    public UnixTimeUtc TransitCreated { get; set; }

    public UnixTimeUtc TransitUpdated { get; set; }

    /// <summary>
    /// If true, the payload is encrypted by the client.  In reality, you SHOULD to encrypt all
    /// data yet there are use cases where we need anonymous users to read data (i.e. some profile attributes, etc.)
    /// </summary>
    public bool IsEncrypted { get; set; }

    /// <summary>
    /// The OdinId of the DI that sent this file.
    /// </summary>
    public string SenderOdinId { get; set; }

    /// <summary>
    /// The identity that originally created this file; even if it was sent around.
    ///
    /// This is only nullable because of existing production data
    /// </summary>
    public OdinId? OriginalAuthor { get; set; }

    public AppFileMetaData AppData { get; set; } = new();

    public LocalAppMetadata LocalAppData { get; set; } = new();

    public GlobalTransitIdFileIdentifier ReferencedFile { get; set; }

    public ReactionSummary ReactionPreview { get; set; }

    public Guid VersionTag { get; set; }

    public List<PayloadDescriptor> Payloads { get; set; }

    public DataSource DataSource { get; set; }

    /// <summary>
    /// When this file expires; see <see cref="FileTtl"/>. Zero means never. Exposed so a client can
    /// show a countdown without a second round trip.
    /// </summary>
    public long Ttl { get; set; }

    public PayloadDescriptor GetPayloadDescriptor(string key)
    {
        return Payloads?.SingleOrDefault(p => p.KeyEquals(key));
    }
}