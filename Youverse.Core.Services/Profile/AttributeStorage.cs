using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security;
using System.Threading.Tasks;
using Youverse.Core.Identity.DataAttribute;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Profile
{
    /// <summary>
    /// Specialized storage for the DI Owner's data attributes.  This does not enforce data
    /// security.  That should be done by the consuming services.
    /// </summary>
    internal sealed class AttributeStorage
    {
        private const string CategoryAttributeStorageCollection = "cdas";
        private const string AttributeStorageCollection = "das";

        private readonly DotYouContextAccessor _contextAccessor;
        private readonly ISystemStorage _systemStorage;

        public AttributeStorage(DotYouContextAccessor contextAccessor, ISystemStorage systemStorage)
        {
            _contextAccessor = contextAccessor;
            _systemStorage = systemStorage;
        }

        public async Task<PagedResult<DataAttributeCategory>> GetCategories(PageOptions pageOptions)
        {
            AssertCallerIsOwner();
            var results = await _systemStorage.WithTenantSystemStorageReturnList<DataAttributeCategory>(CategoryAttributeStorageCollection, s => s.GetList(pageOptions));
            return results;
        }

        public Task SaveCategory(DataAttributeCategory category)
        {
            _systemStorage.WithTenantSystemStorage<DataAttributeCategory>(CategoryAttributeStorageCollection, s => s.Save(category));
            return Task.CompletedTask;
        }

        public Task DeleteCategory(Guid id)
        {
            _systemStorage.WithTenantSystemStorage<DataAttributeCategory>(CategoryAttributeStorageCollection, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public Task SaveAttribute(BaseAttribute attribute)
        {
            _systemStorage.WithTenantSystemStorage<BaseAttribute>(AttributeStorageCollection, s => s.Save(attribute));
            return Task.CompletedTask;
        }

        public Task DeleteAttribute(Guid id)
        {
            _systemStorage.WithTenantSystemStorage<DataAttributeCategory>(AttributeStorageCollection, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => true;
            var results = await _systemStorage.WithTenantSystemStorageReturnList<BaseAttribute>(AttributeStorageCollection, s => s.Find(predicate, pageOptions));
            return results;
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions, Guid categoryId)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => attr.CategoryId == categoryId;
            var results = await _systemStorage.WithTenantSystemStorageReturnList<BaseAttribute>(AttributeStorageCollection, s => s.Find(predicate, pageOptions));
            return results;
        }

        public async Task<BaseAttribute> GetAttributeByType(int type, Guid categoryId)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => attr.AttributeType == type && attr.CategoryId == categoryId;
            var results = await _systemStorage.WithTenantSystemStorageReturnSingle<BaseAttribute>(AttributeStorageCollection, s => s.FindOne(predicate));
            return results;
        }


        public Task SaveAttributeCollection(Guid id, IList<BaseAttribute> attributes)
        {
            string collection = GetAttributeCollectionStorageName(id);
            foreach (var attr in attributes)
            {
                _systemStorage.WithTenantSystemStorage<BaseAttribute>(collection, s => s.Save(attr));
            }

            return Task.CompletedTask;
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributeCollection(Guid id, PageOptions pageOptions)
        {
            string collection = GetAttributeCollectionStorageName(id);
            var results = await _systemStorage.WithTenantSystemStorageReturnList<BaseAttribute>(collection, s => s.GetList(pageOptions));
            return results;
        }


        public async Task<IList<BaseAttribute>> GetAttributeCollectionSubset(Guid attributeCollectionId, IEnumerable<Guid> attributeIdList)
        {
            string collection = GetAttributeCollectionStorageName(attributeCollectionId);
            Expression<Func<BaseAttribute, bool>> predicate = attr => attributeIdList.Contains(attr.Id);
            var page = await _systemStorage.WithTenantSystemStorageReturnList<BaseAttribute>(collection, s => s.Find(predicate, PageOptions.Default));
            return page.Results;
        }

        private string GetAttributeCollectionStorageName(Guid id)
        {
            return $"d{id:N}";
        }


        private void AssertCallerIsOwner()
        {
            if (this._contextAccessor.GetCurrent().Caller.IsOwner == false)
            {
                throw new SecurityException("Caller must be owner");
            }
        }
    }
}