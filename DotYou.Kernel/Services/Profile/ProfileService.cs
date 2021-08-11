using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
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

        public ProfileService(DotYouContext context, ILogger<ProfileService> logger) : base(context, logger, null, null)
        {
        }

        public async Task<HumanProfile> Get(DotYouIdentity dotYouId)
        {
            //TODO:
            // 1 return from cache 
            // 2 if not in cache - pull from DI and update local cache
            HumanProfile profile = null;

            profile = await WithTenantStorageReturnSingle<HumanProfile>(PROFILE_DATA_COLLECTION, s => s.Get(dotYouId.Id));

            if (null != profile)
            {
                return profile;
            }

            //maybe this should be a call from the cache
            var response = await this.CreatePerimeterHttpClient(dotYouId).GetProfile();
            if (response.IsSuccessStatusCode && response.Content != null)
            {
                var ownerProfile = response.Content;
                
                //TODO: drop ownerprofile 
                profile = new HumanProfile()
                {
                    Name = ownerProfile.Name,
                    AvatarUri = ownerProfile.AvatarUri,
                    // PublicKeyCertificate =  
                };
                
                //TODO: need to add a last saved date last cached date
                await this.Save(profile);
                
                return profile;
            }

            return null;
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