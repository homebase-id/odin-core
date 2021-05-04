using DotYou.Kernel;
using DotYou.Kernel.Services;
using DotYou.Types;
using Microsoft.Extensions.Logging;
using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Identity.Web.Services.Contacts
{
    public class ContactService : DotYouServiceBase, IContactService
    {
        const string CONTACT_COLLECTION = "contacts";

        public ContactService(DotYouContext context, ILogger<ContactService> logger) : base(context, logger) { }

        public Task<Contact> Get(Guid id)
        {
            var result = WithTenantStorageReturnSingle<Contact>(CONTACT_COLLECTION, storage => storage.Get(id));
            return result;
        }

        public Task Save(Contact contact)
        {
            WithTenantStorage<Contact>(CONTACT_COLLECTION,storage => storage.Save(contact));
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

        public Task<Contact> GetByDomainName(string domainName)
        {

            throw new NotImplementedException();
        }

        /*
        public async Task AddToNetwork(Contact contact)
        {
            if(contact.RelationshipId == Guid.Empty)
            {
                //TODO: this is being swallowed for some reason
                throw new InvalidOperationException("RelationshipId cannot be an Empty Guid when adding a contact to your network");
            }

            //TODO: address deduplication

            await this.Save(contact);

        }

        public async Task AddToNetwork(AcceptedConnectionRequest acknowledgment)
        {
            var contact = new Contact()
            {
                DotYouId = acknowledgment.OriginalConnectionRequest.Recipient,
                GivenName = acknowledgment.RecipientGivenName,
                Surname = acknowledgment.RecipientSurname,
                RelationshipId = acknowledgment.RelationshipId
            };

            await this.AddToNetwork(contact);
        }
        */
    }
}
