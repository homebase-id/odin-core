using System.Threading.Tasks;
using DotYou.Types.Circle;

namespace DotYou.Types.SignalR
{
    public interface INotificationHub
    {
        Task ConnectionRequestReceived(ConnectionRequest request);

        Task ConnectionRequestAccepted(AcknowledgedConnectionRequest request);
    }

}
