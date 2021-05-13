using System.Threading.Tasks;
using DotYou.Types.Circle;

namespace DotYou.Types.SignalR
{
    public interface INotificationHub
    {
        //Task NotificationOfCircleInvite(CircleInvite circleInvite);

        Task ConnectionRequestReceived(ConnectionRequest request);

        Task ConnectionRequestAccepted(EstablishConnectionRequest request);
    }

}
