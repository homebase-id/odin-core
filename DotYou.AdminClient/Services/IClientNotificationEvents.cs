using System;
using DotYou.Types.Circle;
using DotYou.Types.Messaging;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace DotYou.AdminClient.Services
{
    /// <summary>
    /// Sends notifications throughout the client app
    /// </summary>
    public interface IClientNotificationEvents
    {
        //event Action<CircleInvite> CircleInviteReceived;

        event Action<Message> NewEmailReceived;

        event Action<ChatMessageEnvelope> NewChatMessageReceived;

        event Action<ConnectionRequest> ConnectionRequestReceived;

        event Action<EstablishConnectionRequest> ConnectionRequestAccepted;

        void BroadcastNewEmailReceived(Message message);

        void BroadcastNewChatMessageReceived(ChatMessageEnvelope message);
        
        void BroadcastConnectionRequestReceived(ConnectionRequest request);

        //void BroadcastCircleInviteReceived(CircleInvite invite);

        void BroadcastConnectionRequestAccepted(EstablishConnectionRequest acceptedRequest);
    }
}
