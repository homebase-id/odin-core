
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Base;
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
    
    public SensitiveByteArray EccSalt { get; set; }
    public string EccPublicKey { get; set; }
    public SharedSecretEncryptedPayload EncryptedPayload { get; set; }
}

public class EncryptedFeedItemPayload
{
    public KeyHeader KeyHeader { get; set; }
    public OdinId AuthorOdinId { get; set; }
}

public enum FeedDistroType
{
    UnencryptedFileMetadata = 2,
    EncryptedFileMetadata = 4
}