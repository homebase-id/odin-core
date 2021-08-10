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
        /// Retrieves a profile by their <see cref="DotYouIdentity"/>
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<HumanConnectionProfile> Get(DotYouIdentity dotYouId);
        
        /// <summary>
        /// Upserts a profile into the system.
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        Task Save(HumanConnectionProfile profile);

        /// <summary>
        /// Deletes the specified profile
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task Delete(DotYouIdentity dotYouId);

        /// <summary>
        /// Gets connections from the system
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        Task<PagedResult<HumanConnectionProfile>> GetConnections(PageOptions req);

        /// <summary>
        /// Finds contacts matching the given predicate.
        /// </summary>
        Task<PagedResult<HumanConnectionProfile>> Find(Expression<Func<HumanConnectionProfile, bool>> predicate, PageOptions req);

     
    }
}
