using System;
using DotYou.Types.Circle;

namespace DotYou.AdminClient.Services
{
    /// <summary>
    /// Sends notifications throughout the client app
    /// </summary>
    public interface IClientNotificationEvents
    {
        //event Action<CircleInvite> CircleInviteReceieved;

        event Action<ConnectionRequest> ConnectionRequestReceived;

        event Action<EstablishConnectionRequest> ConnectionRequestAccepted;

        void BroadcastConnectionRequestRecieved(ConnectionRequest request);

        //void BroadcastCircleInviteReceived(CircleInvite invite);

        void BroadcastConnectionRequestAccepted(EstablishConnectionRequest acceptedRequest);
    }
}
