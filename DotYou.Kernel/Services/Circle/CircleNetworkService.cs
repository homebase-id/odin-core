using Dawn;
using System;
using DotYou.Types;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services.Contacts;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace DotYou.Kernel.Services.Circle
{
    //Need to consider using the recipient public key instead of the dotyouid
    //meaning i can go to frodo site, click connect and the public ke cert has all i need to
    //make the connect-request as well as encrypt the request.

    //see: DotYouClaimTypes.PublicKeyCertificate

    //can I get SAMs public key certificate from the request of the original client cert auth

    public class CircleNetworkService : DotYouServiceBase, ICircleNetworkService
    {
        const string PENDING_CONNECTION_REQUESTS = "ConnectionRequests";
        const string SENT_CONNECTION_REQUESTS = "SentConnectionRequests";

        private readonly IHumanConnectionProfileService _profileService;

        public CircleNetworkService(DotYouContext context, IHumanConnectionProfileService profileService, ILogger<CircleNetworkService> logger, IHubContext<NotificationHub, INotificationHub> hub, DotYouHttpClientFactory fac) : base(context, logger, hub, fac)
        {
            _profileService = profileService;
        }
        
        public async Task<SystemCircle> GetSystemCircle(DotYouIdentity dotYouId)
        {
            var contact = await _profileService.Get(dotYouId);

            if (contact == null)
            {
                //TODO: I wonder if we should throw an exception because this is inaccurate
                return SystemCircle.PublicAnonymous;
            }

            return contact.SystemCircle;
        }

        public Task<bool> Disconnect(DotYouIdentity dotYouId)
        {
            throw new NotImplementedException();
        }

        public Task<bool> Block(DotYouIdentity dotYouId)
        {
            throw new NotImplementedException();
        }
        
        public Task<bool> Unblock(DotYouIdentity dotYouId)
        {
            throw new NotImplementedException();
        }


    }
}