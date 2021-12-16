using System;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Identity.DataAttribute;

namespace Youverse.Core.Services.Profile
{
    /// <summary>
    /// Enables the definition and management of various data attributes
    /// describing a <see cref="DotYouProfile"/>
    /// </summary>
    public interface IDataAttributeManagementService
    {
        /// <summary>
        /// Gets a list of categories
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<DataAttributeCategory>> GetCategories(PageOptions pageOptions);

        /// <summary>
        /// Creates or updates the specified category
        /// </summary>
        /// <param name="category"></param>
        /// <returns></returns>
        Task SaveCategory(DataAttributeCategory category);

        /// <summary>
        /// Deletes the specified category.  Any data attributes in this category will remain unchanged
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task DeleteCategory(Guid id);

        /// <summary>
        /// Creates or updates the specified <see cref="DataAttribute"/>
        /// </summary>
        /// <returns></returns>
        Task SaveAttribute(BaseAttribute attribute);

        /// <summary>
        /// Deletes the specified attribute
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task DeleteAttribute(Guid id);

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
        /// Returns a <see cref="PagedResult{T}"/> of attributes for a given collection
        /// </summary>
        /// <param name="id"></param>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        Task<PagedResult<BaseAttribute>> GetAttributeCollection(Guid id, PageOptions pageOptions);

        /// <summary>
        /// Saves a set of attributes under a given id
        /// </summary>
        /// <param name="id"></param>
        /// <param name="attributes"></param>
        /// <returns></returns>
        Task SaveAttributeCollection(Guid id, params BaseAttribute[] attributes);
    }
}