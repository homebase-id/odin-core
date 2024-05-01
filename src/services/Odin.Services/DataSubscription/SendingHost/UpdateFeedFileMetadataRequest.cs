using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.DataSubscription.SendingHost;

public class UpdateFeedFileMetadataRequest
{
    public GlobalTransitIdFileIdentifier FileId { get; set; }

    public FileMetadata FileMetadata { get; set; }
    
    public SharedSecretEncryptedPayload EncryptedPayload { get; set; }
    public string SenderEccPublicKey { get; set; }
    public byte[] EccSalt { get; set; }
}