using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Contacts
{
    /// <summary>
    /// <inheritdoc cref="IProfileService"/>
    /// </summary>
    public class ProfileService : DotYouServiceBase, IProfileService
    {
        private const string PROFILE_DATA_COLLECTION = "hcp";

        public ProfileService(DotYouContext context, ILogger<ProfileService> logger) : base(context, logger, null, null)
        {
        }

        public async Task<HumanProfile> Get(DotYouIdentity dotYouId)
        {
            var profile = await WithTenantStorageReturnSingle<HumanProfile>(PROFILE_DATA_COLLECTION, s => s.Get(dotYouId.Id));
            return profile;
        }

        public Task Save(HumanProfile profile)
        {
            WithTenantStorage<HumanProfile>(PROFILE_DATA_COLLECTION, storage => storage.Save(profile));
            return Task.CompletedTask;
        }

        public async Task<PagedResult<HumanProfile>> Find(Expression<Func<HumanProfile, bool>> predicate, PageOptions req)
        {
            var results = await WithTenantStorageReturnList<HumanProfile>(PROFILE_DATA_COLLECTION, s => s.Find(predicate, req));
            return results;
        }

        public Task Delete(DotYouIdentity dotYouId)
        {
            WithTenantStorage<HumanProfile>(PROFILE_DATA_COLLECTION, s => s.Delete(dotYouId));
            return Task.CompletedTask;
        }
    }
}