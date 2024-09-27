
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.EncryptionKeyService;
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
    
    public EccEncryptedPayload EncryptedPayload { get; set; }
}

public class FeedItemPayload
{
    public byte[] KeyHeaderBytes { get; set; }
    
    public OdinId? CollaborationChannelSender { get; set; }
}

public enum FeedDistroType
{
    Normal = 2,
    CollaborativeChannel = 4
}