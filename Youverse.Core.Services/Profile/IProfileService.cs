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
        /// Retrieves a profile by their <see cref="DotYouIdentity"/>.  This will pull a local
        /// cached copy if it exists, otherwise it will retrieve from the <see cref="DotYouIdentity"/> DI
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<DotYouProfile> Get(DotYouIdentity dotYouId);
        
        /// <summary>
        /// Upserts a profile into the system.
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        Task Save(DotYouProfile profile);

        /// <summary>
        /// Deletes the specified profile
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task Delete(DotYouIdentity dotYouId);

        /// <summary>
        /// Finds contacts matching the given predicate.
        /// </summary>
        Task<PagedResult<DotYouProfile>> Find(Expression<Func<DotYouProfile, bool>> predicate, PageOptions req);

     
    }
}
