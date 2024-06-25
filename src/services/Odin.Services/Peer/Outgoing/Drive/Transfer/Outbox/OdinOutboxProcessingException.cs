using System;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

public class OdinOutboxProcessingException : OdinException
{
    public OdinOutboxProcessingException(string message) : base(message)
    {
    }

    public OdinOutboxProcessingException(string message, Exception inner) : base(message, inner)
    {
    }
    
    public InternalDriveFileId File { get; set; }

    public OdinId Recipient { get; set; }
    
    public LatestTransferStatus TransferStatus { get; set; }
        
    /// <summary>
    /// Indicates the version of the file that was sent
    /// </summary>
    public Guid VersionTag { get; set; }
    
    public Guid? GlobalTransitId { get; set; }
}