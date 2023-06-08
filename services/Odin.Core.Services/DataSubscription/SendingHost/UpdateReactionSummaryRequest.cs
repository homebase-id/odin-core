using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Storage;

namespace Youverse.Core.Services.DataSubscription.SendingHost;

public class UpdateReactionSummaryRequest
{
    public GlobalTransitIdFileIdentifier FileId { get; set; }
    public ReactionSummary ReactionPreview { get; set; }
}

public class UpdateFeedFileMetadataRequest
{
    public GlobalTransitIdFileIdentifier FileId { get; set; }
    public FileMetadata FileMetadata { get; set; }
}