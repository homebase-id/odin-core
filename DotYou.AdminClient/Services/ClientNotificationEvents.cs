using System;
using DotYou.Types.Circle;

namespace DotYou.AdminClient.Services
{
    public class ClientNotificationEvents : IClientNotificationEvents
    {
        //public event Action<CircleInvite> CircleInviteReceieved;

        public event Action<ConnectionRequest> ConnectionRequestReceived;

        public event Action<EstablishConnectionRequest> ConnectionRequestAccepted;
       

        // public void BroadcastCircleInviteReceived(CircleInvite invite)
        // {
        //     CircleInviteReceieved?.Invoke(invite);
        // }

        public void BroadcastConnectionRequestAccepted(EstablishConnectionRequest acceptedRequest)
        {
            ConnectionRequestAccepted?.Invoke(acceptedRequest);
        }

        public void BroadcastConnectionRequestRecieved(ConnectionRequest request)
        {
            Console.WriteLine($"Broadcast incoming request from {request.Sender}");
            ConnectionRequestReceived?.Invoke(request);
        }
    }
}
