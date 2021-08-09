using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DotYou.Types;

namespace DotYou.Kernel.Services.Contacts
{
    /// <summary>
    /// Services for managing, importing, and connecting with humans.
    /// </summary>
    public interface IHumanConnectionProfileService
    {
        /// <summary>
        /// Retrieves the contact by a given Id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<HumanConnectionProfile> Get(Guid id);

        /// <summary>
        /// Upserts a contact into the system.
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        Task Save(HumanConnectionProfile profile);

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
        Task<PagedResult<HumanConnectionProfile>> GetContacts(PageOptions req, bool connectedContactsOnly);

        /// <summary>
        /// Finds contacts matching the given predicate.
        /// </summary>
        Task<PagedResult<HumanConnectionProfile>> FindContacts(Expression<Func<HumanConnectionProfile, bool>> predicate, PageOptions req);

        /// <summary>
        /// Retrieves a contact by their domain name
        /// </summary>
        /// <param name="domainName"></param>
        /// <returns></returns>
        Task<HumanConnectionProfile> GetByDotYouId(string domainName);
    }
}
