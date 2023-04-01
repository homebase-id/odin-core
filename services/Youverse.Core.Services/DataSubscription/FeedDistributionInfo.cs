using Youverse.Core.Identity;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Storage;

public class FeedDistributionInfo 
{
    public OdinId OdinId { get; set; }
}

public class ReactionPreviewDistributionItem 
{
    public DriveNotificationType DriveNotificationType { get; set; }
    public InternalDriveFileId SourceFile { get; set; }
    public FileSystemType FileSystemType { get; set; }
}