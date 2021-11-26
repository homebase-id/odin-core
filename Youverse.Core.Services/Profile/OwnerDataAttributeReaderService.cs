using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Identity.DataAttribute;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;

namespace Youverse.Core.Services.Profile
{
    /// <inheritdoc cref="IOwnerDataAttributeReaderService"/>
    public class OwnerDataAttributeReaderService : DotYouServiceBase<IOwnerDataAttributeReaderService>, IOwnerDataAttributeReaderService
    {
        private readonly ICircleNetworkService _circleNetwork;
        private readonly OwnerDataAttributeStorage<IOwnerDataAttributeReaderService> _das;

        public OwnerDataAttributeReaderService(DotYouContext context, ILogger<IOwnerDataAttributeReaderService> logger, ICircleNetworkService circleNetwork) : base(context, logger, null, null)
        {
            _circleNetwork = circleNetwork;
            _das = new OwnerDataAttributeStorage<IOwnerDataAttributeReaderService>(context, logger);
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

        public async Task<DotYouProfile> GetProfile()
        {
            OwnerProfile oProfile = null;
            if (await _circleNetwork.IsConnected(Context.Caller.DotYouId))
            {
                oProfile = await _das.GetConnectedProfile();
            }
            else
            {
                oProfile = await _das.GetPublicProfile();
            }

            var profile = new DotYouProfile()
            {
                DotYouId = Context.HostDotYouId,
                Name = oProfile?.Name,
                AvatarUri = oProfile?.Photo?.ProfilePic
            };

            return profile;
        }
    }
}