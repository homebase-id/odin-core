using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DotYou.Types;

namespace DotYou.Kernel.Services.Contacts
{
    /// <summary>
    /// Services for managing, importing, and connecting with contacts.
    /// </summary>
    public interface IPersonService
    {
        /// <summary>
        /// Retreives the contact by a given Id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<Person> Get(Guid id);

        /// <summary>
        /// Upserts a contact into the system.
        /// </summary>
        /// <param name="person"></param>
        /// <returns></returns>
        Task Save(Person person);

        /// <summary>
        /// Deletes the specified contact
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task Delete(Guid id);

        /// <summary>
        /// Gets contacts from the system
        /// </summary>
        /// <param name="req"></param>
        /// <param name="connectedContactsOnly">If true, only returns connects with which I am connected.</param>
        /// <returns></returns>
        Task<PagedResult<Person>> GetContacts(PageOptions req, bool connectedContactsOnly);

        /// <summary>
        /// Finds contacts matching the given predicate.
        /// </summary>
        Task<PagedResult<Person>> FindContacts(Expression<Func<Person, bool>> predicate, PageOptions req);

        /// <summary>
        /// Retrieves a contact by their domain name
        /// </summary>
        /// <param name="domainName"></param>
        /// <returns></returns>
        Task<Person> GetByDotYouId(string domainName);
    }
}
