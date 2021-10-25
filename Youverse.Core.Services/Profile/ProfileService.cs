﻿using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Profile
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

            profile = await WithTenantSystemStorageReturnSingle<DotYouProfile>(PROFILE_DATA_COLLECTION, s => s.Get(dotYouId));

            if (null != profile)
            {
                Console.WriteLine($"Cached Profile found {dotYouId}");
                return profile;
            }

            Console.WriteLine($"Retrieving remote profile for {dotYouId}");

            //maybe this should be a call from the cache
            var client = this.CreatePerimeterHttpClient(dotYouId);
            var response = await client.GetProfile();

            if (response.IsSuccessStatusCode && response.Content != null)
            {
                profile = response.Content;

                Console.WriteLine($"Profile retrieved: Name is: {profile.Name}");

                //TODO: need to add a last saved date last cached date
                await this.Save(profile);
                return profile;
            }

            return null;
        }

        public async Task<byte[]> GetPublicKeyForKeyHeader(DotYouIdentity dotYouId, bool forceRefresh = false)
        {
            //TODO: optimize by reading a dictionary cache
            
            var profile = await this.Get(dotYouId);

            if (profile == null)
            {
                return null;
            }

            return string.IsNullOrEmpty(profile.PublicKeyCertificate) ? null : Convert.FromBase64String(profile.PublicKeyCertificate);
        }

        public Task Save(DotYouProfile profile)
        {
            WithTenantSystemStorage<DotYouProfile>(PROFILE_DATA_COLLECTION, storage => storage.Save(profile));
            return Task.CompletedTask;
        }

        public async Task<PagedResult<DotYouProfile>> Find(Expression<Func<DotYouProfile, bool>> predicate, PageOptions req)
        {
            var results = await WithTenantSystemStorageReturnList<DotYouProfile>(PROFILE_DATA_COLLECTION, s => s.Find(predicate, req));
            return results;
        }

        public Task Delete(DotYouIdentity dotYouId)
        {
            WithTenantSystemStorage<DotYouProfile>(PROFILE_DATA_COLLECTION, s => s.Delete(dotYouId));
            return Task.CompletedTask;
        }
    }
}