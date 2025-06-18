using System;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.EncryptionKeyService;

namespace Odin.Services.DataSubscription.SendingHost;

public class UpdateFeedFileMetadataRequest
{
    public GlobalTransitIdFileIdentifier FileId { get; set; }

    public FileMetadata FileMetadata { get; set; }
    
    public EccEncryptedPayload EncryptedPayload { get; set; }
    public FeedDistroType FeedDistroType { get; set; }
    public Guid? UniqueId { get; set; }
    public RemotePayloadInfo RemotePayloadInfo { get; set; }
}