using System;
using DotYou.Types.Circle;
using DotYou.Types.Messaging;

namespace DotYou.AdminClient.Services
{
    public class ClientNotificationEvents : IClientNotificationEvents
    {
        //public event Action<CircleInvite> CircleInviteReceived;

        public event Action<Message> NewEmailReceived;
        public event Action<ConnectionRequest> ConnectionRequestReceived;

        public event Action<EstablishConnectionRequest> ConnectionRequestAccepted;
       

        // public void BroadcastCircleInviteReceived(CircleInvite invite)
        // {
        //     CircleInviteReceived?.Invoke(invite);
        // }

        public void BroadcastConnectionRequestAccepted(EstablishConnectionRequest acceptedRequest)
        {
            ConnectionRequestAccepted?.Invoke(acceptedRequest);
        }

        public void BroadcastNewEmailReceived(Message message)
        {
            NewEmailReceived?.Invoke(message);
        }

        public void BroadcastConnectionRequestReceived(ConnectionRequest request)
        {
            //Console.WriteLine($"Broadcast incoming request from {request.SenderDotYouId}");
            ConnectionRequestReceived?.Invoke(request);
        }
    }
}
