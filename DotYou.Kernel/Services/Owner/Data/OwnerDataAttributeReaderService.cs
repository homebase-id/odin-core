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
    /// <inheritdoc cref="IOwnerDataAttributeReaderService"/>
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
            if (await _circleNetwork.IsConnected(Context.Caller.DotYouId) )
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