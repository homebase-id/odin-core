using System.Threading.Tasks;
using Youverse.Core.Services.Contacts.Circle;

namespace Youverse.Core.Services.Base
{
    public interface INotificationHub
    {
        Task ConnectionRequestReceived(ConnectionRequest request);

        Task ConnectionRequestAccepted(AcknowledgedConnectionRequest request);
    }

}
