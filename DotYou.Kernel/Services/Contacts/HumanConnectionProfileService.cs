using System;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Contacts
{
    public class HumanConnectionProfileService : DotYouServiceBase, IHumanConnectionProfileService
    {
        const string DATA_COLLECTION = "hcp";

        public HumanConnectionProfileService(DotYouContext context, ILogger<HumanConnectionProfileService> logger) : base(context, logger, null, null)
        {
        }

        public Task<HumanConnectionProfile> Get(Guid id)
        {
            var result = WithTenantStorageReturnSingle<HumanConnectionProfile>(DATA_COLLECTION, storage => storage.Get(id));
            return result;
        }

        public async Task Save(HumanConnectionProfile profile)
        {
            //TODO: need to revist this merge process to be more explicit for the caller who has the context of what they want to do 

            HumanConnectionProfile existingProfile = null;

            //if we find a record by their dotYouId, save it and overwrite everything else
            existingProfile = await GetByDotYouId(profile.DotYouId);
            if (existingProfile != null)
            {
                existingProfile.GivenName = profile.GivenName;
                existingProfile.Surname = profile.Surname;
                existingProfile.Tag = profile.Tag;
                existingProfile.PrimaryEmail = profile.PrimaryEmail;
                WithTenantStorage<HumanConnectionProfile>(DATA_COLLECTION, storage => storage.Save(existingProfile));
                return;
            }

            existingProfile = await GetByExactNameMatch(profile);
            if (existingProfile != null)
            {
                existingProfile.GivenName = profile.GivenName;
                existingProfile.Surname = profile.Surname;
                existingProfile.Tag = profile.Tag;
                existingProfile.PrimaryEmail = profile.PrimaryEmail;
                WithTenantStorage<HumanConnectionProfile>(DATA_COLLECTION, storage => storage.Save(existingProfile));
                return;
            }

            //otherwise just save it by the id

            WithTenantStorage<HumanConnectionProfile>(DATA_COLLECTION, storage => storage.Save(profile));
        }

        public async Task<PagedResult<HumanConnectionProfile>> GetContacts(PageOptions req, bool connectedContactsOnly)
        {
            Expression<Func<HumanConnectionProfile, string>> sortKeySelector = key => key.GivenName;
            Expression<Func<HumanConnectionProfile, bool>> predicate = connectedContactsOnly ? c => c.SystemCircle == SystemCircle.Connected : c => true; //HACK: need to update the storage provider GetList method
            
            PagedResult<HumanConnectionProfile> results = await WithTenantStorageReturnList<HumanConnectionProfile>(DATA_COLLECTION, s => s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, req));

            return results;
            
            //
            // if (connectedContactsOnly)
            // {
            //     results = await WithTenantStorageReturnList<Contact>(CONTACT_COLLECTION, s => s.Find(predicate,ListSortDirection.Ascending, sortKeySelector, req));
            // }
            // else
            // {
            //     // results = await WithTenantStorageReturnList<Contact>(CONTACT_COLLECTION, storage => storage.GetList(req));
            //     
            // }

            return results;
        }

        public async Task<PagedResult<HumanConnectionProfile>> FindContacts(Expression<Func<HumanConnectionProfile, bool>> predicate, PageOptions req)
        {
            var results = await WithTenantStorageReturnList<HumanConnectionProfile>(DATA_COLLECTION, s => s.Find(predicate, req));

            return results;
        }

        public Task Delete(Guid id)
        {
            WithTenantStorage<HumanConnectionProfile>(DATA_COLLECTION, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public async Task<HumanConnectionProfile> GetByDotYouId(string domainName)
        {
            //TODO: need to add support for unique keys in the storage
            var page = await WithTenantStorageReturnList<HumanConnectionProfile>(DATA_COLLECTION, s => s.Find(c => c.DotYouId == domainName && c.PublicKeyCertificate != null, PageOptions.Default));
            return page.Results.SingleOrDefault();
        }

        private async Task<HumanConnectionProfile> GetByExactNameMatch(HumanConnectionProfile humanConnectionProfile)
        {
            Guid id = MiscUtils.MD5HashToGuid(humanConnectionProfile.GivenName + humanConnectionProfile.Surname);
            var page = await WithTenantStorageReturnList<HumanConnectionProfile>(DATA_COLLECTION, s => s.Find(c => c.NameUniqueId == id, PageOptions.Default));
            return page.Results.SingleOrDefault();
        }
    }
}