using Odin.Core.Identity;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.DataSubscription.SendingHost;

public class UpdateFeedFileMetadataRequest
{
    public GlobalTransitIdFileIdentifier FileId { get; set; }
    
    public FileMetadata FileMetadata { get; set; }
    
    public EncryptedKeyHeader SharedSecretEncryptedKeyHeader { get; set; }
    
    public OdinId AuthorOdinId { get; set; }
}