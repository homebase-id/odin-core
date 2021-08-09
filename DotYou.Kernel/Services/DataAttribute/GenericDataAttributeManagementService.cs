using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types;
using DotYou.Types.DataAttribute;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.DataAttribute
{
    /// <summary>
    /// Manages generic data attributes
    /// </summary>
    public class GenericDataAttributeManagementService : DotYouServiceBase, IDataAttributeManagementService
    {
        private readonly string _attributeStorageName;
        private readonly string _categoryStorageName;

        /// <summary>
        /// Initializes an instance 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="logger"></param>
        /// <param name="attributeStorageName">Specifies the collection name where the attributes should be stored</param>
        /// <param name="categoryStorageName">Specifies the collection name where the categories should be stored</param>
        public GenericDataAttributeManagementService(DotYouContext context, ILogger logger, string attributeStorageName, string categoryStorageName) : base(context, logger, null, null)
        {
            _attributeStorageName = attributeStorageName;
            _categoryStorageName = categoryStorageName;
            // _dss = dss;
        }
        
        public async Task<PagedResult<DataAttributeCategory>> GetCategories(PageOptions pageOptions)
        {
            var results = await WithTenantStorageReturnList<DataAttributeCategory>(_categoryStorageName, s => s.GetList(pageOptions));
            return results;
        }

        public Task SaveCategory(DataAttributeCategory category)
        {
            WithTenantStorage<DataAttributeCategory>(_categoryStorageName, s => s.Save(category));
            return Task.CompletedTask;
        }

        public Task DeleteCategory(Guid id)
        {
            WithTenantStorage<DataAttributeCategory>(_categoryStorageName, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public Task SaveAttribute(BaseAttribute attribute)
        {
            WithTenantStorage<BaseAttribute>(_attributeStorageName, s => s.Save(attribute));
            return Task.CompletedTask;
        }

        public Task DeleteAttribute(Guid id)
        {
            WithTenantStorage<DataAttributeCategory>(_attributeStorageName, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => true;
            var results = await WithTenantStorageReturnList<BaseAttribute>(_attributeStorageName, s => s.Find(predicate, pageOptions));

            return results;
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions, Guid categoryId)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => attr.CategoryId == categoryId;
            var results = await WithTenantStorageReturnList<BaseAttribute>(_attributeStorageName, s => s.Find(predicate, pageOptions));
            return results;
        }
    }
}