using Odin.Core.Services.Drives;

namespace Odin.Core.Services.DataSubscription.SendingHost;

public class DeleteFeedFileMetadataRequest
{
    public GlobalTransitIdFileIdentifier FileId { get; set; }
}