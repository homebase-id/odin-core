using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Types.Circle;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DotYou.Kernel.Services
{
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class NotificationHub : Hub<INotificationHub>
    {
        public override Task OnConnectedAsync()
        {
            return base.OnConnectedAsync();
        }
    }
}