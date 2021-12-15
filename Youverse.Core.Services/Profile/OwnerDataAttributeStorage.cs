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
    internal sealed class OwnerDataAttributeStorage
    {
        private const string CategoryAttributeStorageCollection = "cdas";
        private const string AttributeStorageCollection = "das";

        private const string PublicProfileAttributeStorageCollection = "pubprofiledas";
        private const string ConnectedProfileAttributeStorageCollection = "pubprofiledas";

        private readonly DotYouContext _context;
        private readonly ISystemStorage _systemStorage;

        public OwnerDataAttributeStorage(DotYouContext context, ISystemStorage systemStorage)
        {
            _context = context;
            _systemStorage = systemStorage;
        }

        public async Task SavePublicProfile(params BaseAttribute[] attributes)
        {
            foreach (var attr in attributes)
            {
                attr.CategoryId = ProfileConstants.PublicProfileCategoryId;

                var existingAttr = await _systemStorage.WithTenantSystemStorageReturnSingle<BaseAttribute>(PublicProfileAttributeStorageCollection,
                    s => s.FindOne(_attr => _attr.AttributeType == attr.AttributeType && _attr.CategoryId == attr.CategoryId));

                attr.Id = existingAttr?.Id ?? attr.Id;

                _systemStorage.WithTenantSystemStorage<BaseAttribute>(PublicProfileAttributeStorageCollection, s => s.Save(attr));
                // this.SaveAttribute(attr);
            }
        }

        public async Task<PagedResult<BaseAttribute>> GetConnectedProfile(PageOptions pageOptions)
        {
            var page = await this.GetAttributes(pageOptions, ProfileConstants.ConnectedProfileCategoryId);
            return page;
        }

        public async Task SaveConnectedProfile(params BaseAttribute[] attributes)
        {
            foreach (var attr in attributes)
            {
                attr.CategoryId = ProfileConstants.ConnectedProfileCategoryId;

                var existingAttr = await _systemStorage.WithTenantSystemStorageReturnSingle<BaseAttribute>(ConnectedProfileAttributeStorageCollection,
                    s => s.FindOne(_attr => _attr.AttributeType == attr.AttributeType && _attr.CategoryId == attr.CategoryId));

                attr.Id = existingAttr?.Id ?? attr.Id;

                _systemStorage.WithTenantSystemStorage<BaseAttribute>(ConnectedProfileAttributeStorageCollection, s => s.Save(attr));
                // this.SaveAttribute(attr);
            }
        }

        public async Task<PagedResult<BaseAttribute>> GetPublicProfile(PageOptions pageOptions)
        {
            var page = await this.GetAttributes(pageOptions, ProfileConstants.PublicProfileCategoryId);
            return page;
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
        
        //HACK
        public async Task<BaseAttribute> GetPublicProfileAttributeByType(int type, Guid categoryId)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => attr.AttributeType == type && attr.CategoryId == categoryId;
            var results = await _systemStorage.WithTenantSystemStorageReturnSingle<BaseAttribute>(PublicProfileAttributeStorageCollection, s => s.FindOne(predicate));
            return results;
        }
        
        public async Task<BaseAttribute> GetConnectedProfileAttributeByType(int type, Guid categoryId)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => attr.AttributeType == type && attr.CategoryId == categoryId;
            var results = await _systemStorage.WithTenantSystemStorageReturnSingle<BaseAttribute>(ConnectedProfileAttributeStorageCollection, s => s.FindOne(predicate));
            return results;
        }

        public async Task<IList<BaseAttribute>> GetAttributeCollection(IEnumerable<Guid> idList)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => idList.Contains(attr.Id);
            var results = await _systemStorage.WithTenantSystemStorageReturnList<BaseAttribute>(AttributeStorageCollection, s => s.Find(predicate, new PageOptions(1, Int32.MaxValue)));
            return results.Results;
        }

        private void AssertCallerIsOwner()
        {
            if (this._context.Caller.IsOwner == false)
            {
                throw new SecurityException("Caller must be owner");
            }
        }
    }
}