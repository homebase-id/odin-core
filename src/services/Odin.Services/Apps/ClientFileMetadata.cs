using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// Specifies the location of all payloads.  If this is set, there will be no payloads on identity which owns the header
    /// </summary>
    public Drives.DriveCore.Storage.RemotePayloadInfo RemotePayloadInfo { get; set; }

    public PayloadDescriptor GetPayloadDescriptor(string key)
    {
        return Payloads?.SingleOrDefault(p => p.KeyEquals(key));
    }
}