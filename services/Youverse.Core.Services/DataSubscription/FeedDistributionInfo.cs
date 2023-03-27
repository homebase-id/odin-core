using Youverse.Core.Identity;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Mediator;

public class FeedDistributionInfo 
{
    public OdinId OdinId { get; set; }
}

public class FeedDistributionItem 
{
    public DriveNotificationType DriveNotificationType { get; set; }
    public InternalDriveFileId File { get; set; }
}