using System.Threading.Tasks;
using DotYou.Types.Circle;
using DotYou.Types.Messaging;

namespace DotYou.Types.SignalR
{
    public interface INotificationHub
    {
        //Task NotificationOfCircleInvite(CircleInvite circleInvite);

        Task ConnectionRequestReceived(ConnectionRequest request);

        Task ConnectionRequestAccepted(AcknowledgedConnectionRequest request);

        Task NewEmailReceived(Message message);
    }

}
