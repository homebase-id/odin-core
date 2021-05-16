using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Contacts
{
    public class ContactService : DotYouServiceBase, IContactService
    {
        const string CONTACT_COLLECTION = "contacts";

        public ContactService(DotYouContext context, ILogger<ContactService> logger) : base(context, logger, null, null) { }

        public Task<Contact> Get(Guid id)
        {
            var result = WithTenantStorageReturnSingle<Contact>(CONTACT_COLLECTION, storage => storage.Get(id));
            return result;
        }

        public Task Save(Contact contact)
        {
            WithTenantStorage<Contact>(CONTACT_COLLECTION, storage => storage.Save(contact));
            return Task.CompletedTask;
        }

        public async Task<PagedResult<Contact>> GetContacts(PageOptions req)
        {
            var results = await WithTenantStorageReturnList<Contact>(CONTACT_COLLECTION, storage => storage.GetList(req));
            return results;
        }

        public async Task<PagedResult<Contact>> FindContacts(Expression<Func<Contact, bool>> predicate, PageOptions req)
        {
            var results = await WithTenantStorageReturnList<Contact>(CONTACT_COLLECTION, s => s.Find(predicate, req));

            return results;
        }

        public Task Delete(Guid id)
        {
            WithTenantStorage<Contact>(CONTACT_COLLECTION, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public async Task<Contact> GetByDomainName(string domainName)
        {
            //TODO: need to add support for unique keys in the storage
            var page = await WithTenantStorageReturnList<Contact>(CONTACT_COLLECTION, s => s.Find(c => c.DotYouId == domainName, PageOptions.Default));
            return page.Results.SingleOrDefault();
        }
    }
}
