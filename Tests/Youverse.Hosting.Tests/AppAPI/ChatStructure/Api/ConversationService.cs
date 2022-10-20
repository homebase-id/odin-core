using System;
using System.Collections.Generic;
using System.Linq;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

public class Reaction
{
    public string Sender { get; set; }
    public string ReactionValue { get; set; }
}

public class RenderableChatMessage
{
    public RenderableChatMessage()
    {
        Reactions = new List<Reaction>();
    }

    public ChatMessage Message { get; set; }
    public List<Reaction> Reactions { get; init; }
    public UInt64 ReceivedTimestamp { get; set; }
}

public class ConversationService
{
    private readonly Dictionary<Guid, Dictionary<Guid, RenderableChatMessage>> _conversations;
    private readonly ChatServerContext _serverContext;

    public ConversationService(ChatServerContext serverContext)
    {
        _serverContext = serverContext;
        _conversations = new Dictionary<Guid, Dictionary<Guid, RenderableChatMessage>>();
    }

    public void AddMessage(Guid convoId, Guid messageId, UInt64 received, ChatMessage message)
    {
        var messageToAdd = new RenderableChatMessage()
        {
            Message = message,
            ReceivedTimestamp = received
        };

        if (!_conversations.ContainsKey(convoId))
        {
            _conversations.Add(convoId, new Dictionary<Guid, RenderableChatMessage>());
        }

        if (_conversations[convoId].ContainsKey(messageId))
        {
            throw new Exception("message already exists");
        }

        _conversations[convoId].Add(messageId, messageToAdd);
    }

    /// <summary>
    /// Updates the specific message
    /// </summary>
    public void AddReaction(Guid convoId, Guid messageId, Reaction reaction)
    {
        if (_conversations.TryGetValue(convoId, out var group))
        {
            if (group.TryGetValue(messageId, out var message))
            {
                message.Reactions.Add(reaction);
            }
            else
            {
                throw new Exception($"No message with id {messageId} in group {convoId}");
            }
        }
        else
        {
            throw new Exception($"No group with Id {convoId}");
        }
    }

    public List<RenderableChatMessage> GetMessages(Guid convoId)
    {
        if (!_conversations.TryGetValue(convoId, out var chatMessages))
        {
            return null;
        }

        return chatMessages.Values.OrderBy(m => m.ReceivedTimestamp).ToList();
    }
}