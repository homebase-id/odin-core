using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
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
        private const string CATEGORY_ATTRIBUTE_STORAGE = "cdas";
        private const string ATTRIBUTE_STORAGE = "das";

        private readonly DotYouContext _context;
        private readonly ISystemStorage _systemStorage;

        public OwnerDataAttributeStorage(DotYouContext context, ISystemStorage systemStorage)
        {
            _context = context;
            _systemStorage = systemStorage;
        }

        public Task SavePublicProfile(params BaseAttribute[] attributes)
        {
            foreach (var attr in attributes)
            {
                attr.CategoryId = ProfileConstants.PublicProfileCategoryId;
                this.SaveAttribute(attr);
            }

            return Task.CompletedTask;
        }

        public async Task<PagedResult<BaseAttribute>> GetConnectedProfile(PageOptions pageOptions)
        {
            var page = await this.GetAttributes(pageOptions, ProfileConstants.ConnectProfileCategoryId);
            return page;
        }
        
        public Task SaveConnectedProfile(params BaseAttribute[] attributes)
        {
            foreach (var attr in attributes)
            {
                attr.CategoryId = ProfileConstants.ConnectProfileCategoryId;
                this.SaveAttribute(attr);
            }

            return Task.CompletedTask;
        }

        public async Task<PagedResult<BaseAttribute>> GetPublicProfile(PageOptions pageOptions)
        {
            var page = await this.GetAttributes(pageOptions, ProfileConstants.PublicProfileCategoryId);
            return page;
        }
        public async Task<PagedResult<DataAttributeCategory>> GetCategories(PageOptions pageOptions)
        {
            AssertCallerIsOwner();
            var results = await _systemStorage.WithTenantSystemStorageReturnList<DataAttributeCategory>(CATEGORY_ATTRIBUTE_STORAGE, s => s.GetList(pageOptions));
            return results;
        }

        public Task SaveCategory(DataAttributeCategory category)
        {
            _systemStorage.WithTenantSystemStorage<DataAttributeCategory>(CATEGORY_ATTRIBUTE_STORAGE, s => s.Save(category));
            return Task.CompletedTask;
        }

        public Task DeleteCategory(Guid id)
        {
            _systemStorage.WithTenantSystemStorage<DataAttributeCategory>(CATEGORY_ATTRIBUTE_STORAGE, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public Task SaveAttribute(BaseAttribute attribute)
        {
            _systemStorage.WithTenantSystemStorage<BaseAttribute>(ATTRIBUTE_STORAGE, s => s.Save(attribute));
            return Task.CompletedTask;
        }

        public Task DeleteAttribute(Guid id)
        {
            _systemStorage.WithTenantSystemStorage<DataAttributeCategory>(ATTRIBUTE_STORAGE, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => true;
            var results = await _systemStorage.WithTenantSystemStorageReturnList<BaseAttribute>(ATTRIBUTE_STORAGE, s => s.Find(predicate, pageOptions));
            return results;
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions, Guid categoryId)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => attr.CategoryId == categoryId;
            var results = await _systemStorage.WithTenantSystemStorageReturnList<BaseAttribute>(ATTRIBUTE_STORAGE, s => s.Find(predicate, pageOptions));
            return results;
        }
        
        public async Task<BaseAttribute> GetAttributeByType(int type, Guid categoryId)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => attr.CategoryId == categoryId;
            var results = await _systemStorage.WithTenantSystemStorageReturnSingle<BaseAttribute>(ATTRIBUTE_STORAGE, s => s.FindOne(predicate));
            return results;
        }

        public async Task<IList<BaseAttribute>> GetAttributeCollection(IEnumerable<Guid> idList)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => idList.Contains(attr.Id);
            var results = await _systemStorage.WithTenantSystemStorageReturnList<BaseAttribute>(ATTRIBUTE_STORAGE, s => s.Find(predicate, new PageOptions(1, Int32.MaxValue)));
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