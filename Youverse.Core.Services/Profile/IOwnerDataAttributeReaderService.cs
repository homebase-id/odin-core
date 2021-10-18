using System;
using System.Threading.Tasks;
using DotYou.Types;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Identity.DataAttribute;

namespace DotYou.Kernel.Services.Owner.Data
{
    /// <summary>
    /// Supports reading data attributes for a DI owner.  Implementations must ensure only the scope
    /// of data assigned to the caller is returned.  (i.e. if this is frodo's digital identity,
    /// it will ensure onlY those in the fellowship know he has the one ring)
    /// </summary>    
    public interface IOwnerDataAttributeReaderService
    {
        /// <summary>
        /// Gets all attributes
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions);
        
        /// <summary>
        /// Gets all attributes with-in the given category.  Use Guid.Empty
        /// to find attributes not assigned to a category
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <param name="categoryId"></param>
        /// <returns></returns>
        Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions, Guid categoryId);

        /// <summary>
        /// Returns the profile for this DI.  The information included will be based on
        /// the caller's level of security. (i.e. if the caller is in the Public Circle, then
        /// s/he will see only the public profile)
        /// </summary>
        /// <returns></returns>
        Task<DotYouProfile> GetProfile();
    }
}