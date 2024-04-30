
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Drives;
using Odin.Services.Mediator;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.DataSubscription;

public class FeedDistributionInfo 
{
    public OdinId OdinId { get; set; }
}

public class FeedDistributionItem 
{
    public DriveNotificationType DriveNotificationType { get; set; }
    public InternalDriveFileId SourceFile { get; set; }
    public FileSystemType FileSystemType { get; set; }
    
    public FeedDistroType FeedDistroType { get; set; }
    
    public EncryptedKeyHeader SharedSecretEncryptedKeyHeader { get; set; }
    
    public OdinId AuthorOdinId { get; set; }
}

public enum FeedDistroType
{
    UnencryptedFileMetadata = 2,
    EncryptedFileMetadata = 4
}