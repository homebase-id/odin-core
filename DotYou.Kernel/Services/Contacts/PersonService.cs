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
    public class PersonService : DotYouServiceBase, IPersonService
    {
        const string CONTACT_COLLECTION = "contacts";

        public PersonService(DotYouContext context, ILogger<PersonService> logger) : base(context, logger, null, null)
        {
        }

        public Task<Person> Get(Guid id)
        {
            var result = WithTenantStorageReturnSingle<Person>(CONTACT_COLLECTION, storage => storage.Get(id));
            return result;
        }

        public async Task Save(Person person)
        {
            //TODO: need to revist this merge process to be more explicit for the caller who has the context of what they want to do 

            Person existingPerson = null;

            //if we find a record by their dotYouId, save it and overwrite everything else
            existingPerson = await GetByDotYouId(person.DotYouId);
            if (existingPerson != null)
            {
                existingPerson.GivenName = person.GivenName;
                existingPerson.Surname = person.Surname;
                existingPerson.Tag = person.Tag;
                existingPerson.PrimaryEmail = person.PrimaryEmail;
                WithTenantStorage<Person>(CONTACT_COLLECTION, storage => storage.Save(existingPerson));
                return;
            }

            existingPerson = await GetByExactNameMatch(person);
            if (existingPerson != null)
            {
                existingPerson.GivenName = person.GivenName;
                existingPerson.Surname = person.Surname;
                existingPerson.Tag = person.Tag;
                existingPerson.PrimaryEmail = person.PrimaryEmail;
                WithTenantStorage<Person>(CONTACT_COLLECTION, storage => storage.Save(existingPerson));
                return;
            }

            //otherwise just save it by the id

            WithTenantStorage<Person>(CONTACT_COLLECTION, storage => storage.Save(person));
        }

        public async Task<PagedResult<Person>> GetContacts(PageOptions req, bool connectedContactsOnly)
        {
            Expression<Func<Person, string>> sortKeySelector = key => key.GivenName;
            Expression<Func<Person, bool>> predicate = connectedContactsOnly ? c => c.SystemCircle == SystemCircle.Connected : c => true; //HACK: need to update the storage provider GetList method
            
            PagedResult<Person> results = await WithTenantStorageReturnList<Person>(CONTACT_COLLECTION, s => s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, req));

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

        public async Task<PagedResult<Person>> FindContacts(Expression<Func<Person, bool>> predicate, PageOptions req)
        {
            var results = await WithTenantStorageReturnList<Person>(CONTACT_COLLECTION, s => s.Find(predicate, req));

            return results;
        }

        public Task Delete(Guid id)
        {
            WithTenantStorage<Person>(CONTACT_COLLECTION, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public async Task<Person> GetByDotYouId(string domainName)
        {
            //TODO: need to add support for unique keys in the storage
            var page = await WithTenantStorageReturnList<Person>(CONTACT_COLLECTION, s => s.Find(c => c.DotYouId == domainName && c.PublicKeyCertificate != null, PageOptions.Default));
            return page.Results.SingleOrDefault();
        }

        private async Task<Person> GetByExactNameMatch(Person person)
        {
            Guid id = MiscUtils.MD5HashToGuid(person.GivenName + person.Surname);
            var page = await WithTenantStorageReturnList<Person>(CONTACT_COLLECTION, s => s.Find(c => c.NameUniqueId == id, PageOptions.Default));
            return page.Results.SingleOrDefault();
        }
    }
}