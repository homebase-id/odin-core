using System;
using System.Collections.Generic;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Storage;

namespace Youverse.Core.Services.Apps;

public class ClientFileMetadata
{
    public ClientFileMetadata()
    {
        this.AppData = new AppFileMetaData();
    }
    
    public Guid? GlobalTransitId { get; set; }

    public Int64 Created { get; set; }

    public Int64 Updated { get; set; }
    
    public string ContentType { get; set; }

    /// <summary>
    /// If true, the payload is encrypted by the client.  In reality, you SHOULD to encrypt all
    /// data yet there are use cases where we need anonymous users to read data (i.e. some profile attributes, etc.)
    /// </summary>
    public bool PayloadIsEncrypted { get; set; }
        
    /// <summary>
    /// The OdinId of the DI that sent this file.  If null, the file was uploaded by the owner.
    /// </summary>
    public string SenderOdinId { get; set; }

    /// <summary>
    /// The size of the payload on disk
    /// </summary>
    public long PayloadSize { get; set; }
        
    /// <summary>
    /// Specifies the list of recipients set when the file was uploaded
    /// </summary>
    public List<string> OriginalRecipientList { get; set; }
        
    public AppFileMetaData AppData { get; set; }
    
    public GlobalTransitIdFileIdentifier ReferencedFile { get; set; }
}