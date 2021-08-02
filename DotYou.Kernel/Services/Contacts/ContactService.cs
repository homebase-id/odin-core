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
    public class ContactService : DotYouServiceBase, IContactService
    {
        const string CONTACT_COLLECTION = "contacts";

        public ContactService(DotYouContext context, ILogger<ContactService> logger) : base(context, logger, null, null)
        {
        }

        public Task<Contact> Get(Guid id)
        {
            var result = WithTenantStorageReturnSingle<Contact>(CONTACT_COLLECTION, storage => storage.Get(id));
            return result;
        }

        public async Task Save(Contact contact)
        {
            //TODO: need to revist this merge process to be more explicit for the caller who has the context of what they want to do 

            Contact existingContact = null;
            
            //if we find a record by their dotYouId, save it and overwrite everything else
            existingContact = await GetByDotYouId(contact.DotYouId);
            if (existingContact != null)
            {
                existingContact.GivenName = contact.GivenName;
                existingContact.Surname = contact.Surname;
                existingContact.Tag = contact.Tag;
                existingContact.PrimaryEmail = contact.PrimaryEmail;
                WithTenantStorage<Contact>(CONTACT_COLLECTION, storage => storage.Save(existingContact));
                return;
            }

            existingContact = await GetByExactNameMatch(contact);
            if (existingContact != null)
            {
                existingContact.GivenName = contact.GivenName;
                existingContact.Surname = contact.Surname;
                existingContact.Tag = contact.Tag;
                existingContact.PrimaryEmail = contact.PrimaryEmail;
                WithTenantStorage<Contact>(CONTACT_COLLECTION, storage => storage.Save(existingContact));
                return;
            }
            
            //otherwise just save it by the id

            WithTenantStorage<Contact>(CONTACT_COLLECTION, storage => storage.Save(contact));
        }

        public async Task<PagedResult<Contact>> GetContacts(PageOptions req, bool connectedContactsOnly)
        {
            PagedResult<Contact> results;
            if (connectedContactsOnly)
            {
                results = await WithTenantStorageReturnList<Contact>(CONTACT_COLLECTION, s => s.Find(c => c.SystemCircle == SystemCircle.Connected, req));
            }
            else
            {
                results = await WithTenantStorageReturnList<Contact>(CONTACT_COLLECTION, storage => storage.GetList(req));
            }

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

        public async Task<Contact> GetByDotYouId(string domainName)
        {
            //TODO: need to add support for unique keys in the storage
            var page = await WithTenantStorageReturnList<Contact>(CONTACT_COLLECTION, s => s.Find(c => c.DotYouId == domainName && c.PublicKeyCertificate != null, PageOptions.Default));
            return page.Results.SingleOrDefault();
        }
        
        private async Task<Contact> GetByExactNameMatch(Contact contact)
        {
            Guid id = MiscUtils.MD5HashToGuid(contact.GivenName + contact.Surname);
            var page = await WithTenantStorageReturnList<Contact>(CONTACT_COLLECTION, s => s.Find(c => c.NameUniqueId == id, PageOptions.Default));
            return page.Results.SingleOrDefault();
        }
    }
}