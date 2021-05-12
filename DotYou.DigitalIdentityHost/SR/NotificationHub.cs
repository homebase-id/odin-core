using System.Threading.Tasks;
using DotYou.Types.Circle;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace DotYou.TenantHost.SR
{

    public class NotificationHub : Hub<INotificationHub>
    {
        // public Task NotifyOfCircleInvite(CircleInvite circleInvite)
        // {
        //     Clients.All.NotificationOfCircleInvite(circleInvite);
        //
        //     return Task.CompletedTask;
        // }

        public Task NotifyOfConnectionRequest(ConnectionRequest request)
        {
            Clients.All.NotifyOfConnectionRequest(request);
            return Task.CompletedTask;
        }

        public Task NotifyOfConnectionRequestAccepted(EstablishConnectionRequest acceptedRequest)
        {
            Clients.All.NotifyOfConnectionRequestAccepted(acceptedRequest);
            return Task.CompletedTask;
        }
    }
}
