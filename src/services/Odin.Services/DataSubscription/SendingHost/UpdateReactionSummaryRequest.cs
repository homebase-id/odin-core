using Odin.Services.Drives;

namespace Odin.Services.DataSubscription.SendingHost;

public class UpdateReactionSummaryRequest
{
    public GlobalTransitIdFileIdentifier FileId { get; set; }
    public ReactionSummary ReactionPreview { get; set; }
}