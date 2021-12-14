using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Identity.DataAttribute;

namespace Youverse.Core.Services.Profile
{
    /// <summary>
    /// Supports reading data attributes for a DI owner.  Implementations must ensure only the scope
    /// of data assigned to the caller is returned.  (i.e. if this is frodo's digital identity,
    /// it will ensure onlY those in the fellowship know he has the one ring)
    /// </summary>    
    public interface IOwnerDataAttributeReaderService
    {
        /// <summary>
        /// Gets a collection of attributes in the <param name="idList">list</param> if it is visible to the caller
        /// </summary>
        /// <param name="idList"></param>
        /// <returns></returns>
        Task<IList<BaseAttribute>> GetAttributeCollection(IEnumerable<Guid> idList);
        
        /// <summary>
        /// Gets all attributes visible to the caller
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions);
        
        /// <summary>
        /// Gets all attributes with-in the given category visible to the caller.  Use Guid.Empty
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
        Task<PagedResult<BaseAttribute>> GetProfile();
    }
}