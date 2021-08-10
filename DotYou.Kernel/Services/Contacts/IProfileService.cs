using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DotYou.Types;

namespace DotYou.Kernel.Services.Contacts
{
    /// <summary>
    /// Services for managing profiles about with humans with which I'm connected.
    /// </summary>
    public interface IProfileService
    {

        /// <summary>
        /// Retrieves a profile by their <see cref="DotYouIdentity"/>
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<HumanProfile> Get(DotYouIdentity dotYouId);
        
        /// <summary>
        /// Upserts a profile into the system.
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        Task Save(HumanProfile profile);

        /// <summary>
        /// Deletes the specified profile
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task Delete(DotYouIdentity dotYouId);

        /// <summary>
        /// Finds contacts matching the given predicate.
        /// </summary>
        Task<PagedResult<HumanProfile>> Find(Expression<Func<HumanProfile, bool>> predicate, PageOptions req);

     
    }
}
