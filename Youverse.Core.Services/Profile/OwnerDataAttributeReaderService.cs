using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity.DataAttribute;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;

namespace Youverse.Core.Services.Profile
{
    /// <inheritdoc cref="IOwnerDataAttributeReaderService"/>
    public class OwnerDataAttributeReaderService : IOwnerDataAttributeReaderService
    {
        private readonly DotYouContext _context;
        private readonly ILogger<IOwnerDataAttributeReaderService> _logger;
        private readonly ICircleNetworkService _circleNetwork;
        private readonly AttributeStorage _das;
        private readonly ISystemStorage _systemStorage;

        public OwnerDataAttributeReaderService(DotYouContext context, ILogger<IOwnerDataAttributeReaderService> logger, ICircleNetworkService circleNetwork, ISystemStorage systemStorage)
        {
            _context = context;
            _logger = logger;
            _circleNetwork = circleNetwork;
            _systemStorage = systemStorage;
            _das = new AttributeStorage(context, systemStorage);
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributeCollection(Guid id, PageOptions pageOptions)
        {
            var attributes = await _das.GetAttributeCollection(id, pageOptions);
            return attributes;
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions)
        {
            //TODO: update query to filter out attributes the caller should not see
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

        public async Task<PagedResult<BaseAttribute>> GetProfile()
        {
            throw new NotImplementedException("need to re-evaluate this given all of the profile refactoring");
            // if (await _circleNetwork.IsConnected(_context.Caller.DotYouId))
            // {
            //     throw new NotImplementedException("");
            //     //oProfile = await _das.GetConnectedProfile();
            // }
            //
            //
            // return await _das.GetPublicProfile(new PageOptions(1, 100));
        }
        
    }
}