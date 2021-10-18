using System;
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
            Console.WriteLine($"NotificationHub [{this.Context?.User?.Identity?.Name} is connected]");
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            Console.WriteLine($"NotificationHub [{this.Context?.User?.Identity?.Name} is disconnected]");
            return base.OnDisconnectedAsync(exception);
        }
    }
}