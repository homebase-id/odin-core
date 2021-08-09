using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services.Circle;
using DotYou.Kernel.Services.DataAttribute;
using DotYou.Types;
using DotYou.Types.DataAttribute;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Owner.Data
{
    /// <summary>
    /// Supports reading data attributes for a DI owner.  Implementations must ensure only the scope
    /// of data assigned to the caller is returned.  (i.e. if this is frodo's digital identity, it will ensure only
    /// those in the fellowship know he has the one ring)
    /// </summary>
    public class OwnerDataAttributeReaderService : DotYouServiceBase, IOwnerDataAttributeReaderService
    {
        private readonly ICircleNetworkService _circleNetwork;
        private readonly OwnerDataAttributeStorage _das;

        public OwnerDataAttributeReaderService(DotYouContext context, ILogger logger, ICircleNetworkService circleNetwork, IHubContext<NotificationHub, INotificationHub> notificationHub, DotYouHttpClientFactory fac) : base(context, logger, notificationHub, fac)
        {
            _circleNetwork = circleNetwork;
            _das = new OwnerDataAttributeStorage(context, logger);
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions)
        {
            //TODO: update query to filter out attributes the caller should not see
            Expression<Func<BaseAttribute, bool>> predicate = attr => true;
            var attributes = await _das.GetAttributes(pageOptions);
            return attributes;
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions, Guid categoryId)
        {
            //TODO: update query to filter out attributes the caller should not see
            Expression<Func<BaseAttribute, bool>> predicate = attr => attr.CategoryId == categoryId;
            var attributes = await _das.GetAttributes(pageOptions, categoryId);
            return attributes;
        }

        public async Task<Profile> GetProfile()
        {
            var isConnectedWithMe = await _circleNetwork.GetSystemCircle(base.Context.Caller.DotYouId) == SystemCircle.Connected;
            if (isConnectedWithMe)
            {
                var connectedProfile = await _das.GetConnectedProfile();
                
                //HACK: OMG HACK
                connectedProfile.SystemCircle = SystemCircle.Connected;
                return connectedProfile;
            }

            var profile = await _das.GetPublicProfile();

            //HACK: OMG HACK #2
            profile.SystemCircle = SystemCircle.PublicAnonymous;
            return profile;
        }
    }
}