using Odin.Core.Services.Drives;

namespace Odin.Core.Services.DataSubscription.SendingHost;

public class UpdateReactionSummaryRequest
{
    public GlobalTransitIdFileIdentifier FileId { get; set; }
    public ReactionSummary ReactionPreview { get; set; }
}