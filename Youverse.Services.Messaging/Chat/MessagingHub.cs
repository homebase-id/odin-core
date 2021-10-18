using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Youverse.Core.Services.Authorization;

namespace Youverse.Services.Messaging.Chat
{
    [Authorize(Policy = DotYouPolicyNames.IsDigitalIdentityOwner)]
    public class MessagingHub : Hub<IMessagingHub>
    {
        public override Task OnConnectedAsync()
        {
            Console.WriteLine($"ChatHub [{this.Context?.User?.Identity?.Name} is connected]");
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            Console.WriteLine($"ChatHub [{this.Context?.User?.Identity?.Name} is disconnected]");
            return base.OnDisconnectedAsync(exception);
        }
    }
}