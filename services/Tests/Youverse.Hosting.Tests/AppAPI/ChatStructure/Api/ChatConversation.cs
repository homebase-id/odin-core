using System;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

public class ChatConversation
{
    public const int ConversationDefinitionFileType = 8888;

    public Guid Id { get; set; }
    
    public string RecipientOdinId { get; set; }
}