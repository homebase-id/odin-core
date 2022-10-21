using System;
using Youverse.Core;
using Youverse.Hosting.Controllers.ClientToken.App;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

public class SendReactionCommand : CommandBase
{
    public SendReactionCommand()
    {
        Code = CommandCode.SendReaction;
    }
    
    public Guid MessageId { get; set; }
    
    public string ReactionCode { get; set; }
    
    public Guid ConversationId { get; set; }
}

public class JoinConversationCommand : CommandBase
{
    public JoinConversationCommand()
    {
        Code = CommandCode.JoinConversation;
    }
    public Guid ConversationId { get; set; }
}

public class SendReadReceiptCommand : CommandBase
{
    public SendReadReceiptCommand()
    {
        Code = CommandCode.SendReadReceipt;
    }
    public Guid ConversationId { get; set; }
    
    public Guid MessageId { get; set; }

    public UnixTimeUtcMilliseconds Timestamp { get; set; }
}
