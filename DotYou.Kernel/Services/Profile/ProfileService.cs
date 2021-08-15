using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services.Contacts;
using DotYou.Types;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Profile
{
    /// <summary>
    /// <inheritdoc cref="IProfileService"/>
    /// </summary>
    public class ProfileService : DotYouServiceBase, IProfileService
    {
        private const string PROFILE_DATA_COLLECTION = "hcp";

        public ProfileService(DotYouContext context, ILogger<ProfileService> logger, DotYouHttpClientFactory fac) : base(context, logger, null, fac)
        {
        }

        public async Task<DotYouProfile> Get(DotYouIdentity dotYouId)
        {
            //TODO:
            // 1 return from cache 
            // 2 if not in cache - pull from DI and update local cache
            DotYouProfile profile = null;

            profile = await WithTenantStorageReturnSingle<DotYouProfile>(PROFILE_DATA_COLLECTION, s => s.Get(dotYouId));

            if (null != profile)
            {
                return profile;
            }

            //maybe this should be a call from the cache
            var response = await this.CreatePerimeterHttpClient(dotYouId).GetProfile();
            if (response.IsSuccessStatusCode && response.Content != null)
            {
                profile = response.Content;
                
                //TODO: need to add a last saved date last cached date
                await this.Save(profile);
                return profile;
            }

            return null;
        }

        public Task Save(DotYouProfile profile)
        {
            WithTenantStorage<DotYouProfile>(PROFILE_DATA_COLLECTION, storage => storage.Save(profile));
            return Task.CompletedTask;
        }

        public async Task<PagedResult<DotYouProfile>> Find(Expression<Func<DotYouProfile, bool>> predicate, PageOptions req)
        {
            var results = await WithTenantStorageReturnList<DotYouProfile>(PROFILE_DATA_COLLECTION, s => s.Find(predicate, req));
            return results;
        }

        public Task Delete(DotYouIdentity dotYouId)
        {
            WithTenantStorage<DotYouProfile>(PROFILE_DATA_COLLECTION, s => s.Delete(dotYouId));
            return Task.CompletedTask;
        }
    }
}