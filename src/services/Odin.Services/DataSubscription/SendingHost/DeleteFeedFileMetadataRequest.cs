using Odin.Services.Drives;

namespace Odin.Services.DataSubscription.SendingHost;

public class DeleteFeedFileMetadataRequest
{
    public GlobalTransitIdFileIdentifier FileId { get; set; }
}