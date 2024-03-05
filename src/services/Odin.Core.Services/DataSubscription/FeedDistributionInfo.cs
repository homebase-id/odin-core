
using Odin.Core.Identity;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Mediator;
using Odin.Core.Storage;

namespace Odin.Core.Services.DataSubscription;

public class FeedDistributionInfo 
{
    public OdinId OdinId { get; set; }
}

public class ReactionPreviewDistributionItem 
{
    public DriveNotificationType DriveNotificationType { get; set; }
    public InternalDriveFileId SourceFile { get; set; }
    public FileSystemType FileSystemType { get; set; }
    
    public FeedDistroType FeedDistroType { get; set; }
}

public enum FeedDistroType
{
    ReactionPreview = 1,
    FileMetadata = 2
}