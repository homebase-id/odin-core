using System;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

public class SendReactionCommand : CommandBase
{
    public Guid MessageId { get; set; }
    
    public string ReactionCode { get; set; }
    public Guid ConversationId { get; set; }
}

public class JoinConversationCommand : CommandBase
{
    public Guid ConversationId { get; set; }
}
