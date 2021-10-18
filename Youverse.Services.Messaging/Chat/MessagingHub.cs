using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DotYou.Kernel.Services.Authorization;
using DotYou.Types.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DotYou.Kernel.Services.Messaging.Chat
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