using System;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

public class ChatMessage
{
    public const int FileType = 1010;

    public Guid Id { get; set; }
    
    public string Sender { get; set; }

    public Guid ConversationId { get; set; }
    
    public string Text { get; set; }
}