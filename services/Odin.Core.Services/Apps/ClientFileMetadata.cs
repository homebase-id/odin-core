using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Storage;

namespace Odin.Core.Services.Apps;

public class ClientFileMetadata
{
    public ClientFileMetadata()
    {
        this.AppData = new AppFileMetaData();
    }
    
    public Guid? GlobalTransitId { get; set; }

    public Int64 Created { get; set; }

    public Int64 Updated { get; set; }
    
    /// <summary>
    /// If true, the payload is encrypted by the client.  In reality, you SHOULD to encrypt all
    /// data yet there are use cases where we need anonymous users to read data (i.e. some profile attributes, etc.)
    /// </summary>
    public bool IsEncrypted { get; set; }
        
    /// <summary>
    /// The OdinId of the DI that sent this file.  If null, the file was uploaded by the owner.
    /// </summary>
    public string SenderOdinId { get; set; }
    
    public AppFileMetaData AppData { get; set; }
    
    public GlobalTransitIdFileIdentifier ReferencedFile { get; set; }
    public ReactionSummary ReactionPreview { get; set; }
    
    public Guid VersionTag { get; set; }
    
    public List<PayloadDescriptor> Payloads { get; set; }
    
    [Obsolete]
    public IEnumerable<ThumbnailDescriptor> Thumbnails { get; set; }

    public PayloadDescriptor GetPayloadDescriptor(string key)
    {
        return Payloads.SingleOrDefault(pk => pk.Key == key);
    }
}