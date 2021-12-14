using System;
using System.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity.DataAttribute;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Profile
{
    /// <inheritdoc cref="IOwnerDataAttributeManagementService"/>
    public class OwnerDataAttributeManagementService :  IOwnerDataAttributeManagementService
    {
        private readonly DotYouContext _context;
        private readonly OwnerDataAttributeStorage _das;
        private readonly ISystemStorage _systemStorage;

        public OwnerDataAttributeManagementService(DotYouContext context, ILogger<IOwnerDataAttributeManagementService> logger, ISystemStorage systemStorage) 
        {
            _context = context;
            _systemStorage = systemStorage;
            _das = new OwnerDataAttributeStorage(context, systemStorage);
        }

        public async Task<NameAttribute> GetPrimaryName()
        {
            return await _das.GetPrimaryName();
        }

        public Task SavePrimaryName(NameAttribute name)
        {
            return _das.SavePrimaryName(name);
        }

        public Task SaveConnectedProfile(OwnerProfile humanProfile)
        {
            AssertCallerIsOwner();
            return _das.SaveConnectedProfile(humanProfile);
        }

        public async Task<OwnerProfile> GetPublicProfile()
        {
            return await _das.GetPublicProfile();
        }

        public async Task<OwnerProfile> GetConnectedProfile()
        {
            return await _das.GetConnectedProfile();
        }

        public Task SavePublicProfile(OwnerProfile profile)
        {
            AssertCallerIsOwner();
            return _das.SavePublicProfile(profile);
        }

        public async Task<PagedResult<DataAttributeCategory>> GetCategories(PageOptions pageOptions)
        {
            AssertCallerIsOwner();
            return await _das.GetCategories(pageOptions);
        }

        public Task SaveCategory(DataAttributeCategory category)
        {
            AssertCallerIsOwner();
            return _das.SaveCategory(category);
        }

        public Task DeleteCategory(Guid id)
        {
            AssertCallerIsOwner();
            return _das.DeleteCategory(id);
        }

        public Task SaveAttribute(BaseAttribute attribute)
        {
            AssertCallerIsOwner();
            return _das.SaveAttribute(attribute);
        }

        public Task DeleteAttribute(Guid id)
        {
            AssertCallerIsOwner();
            return _das.DeleteAttribute(id);
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions)
        {
            AssertCallerIsOwner();
            return await _das.GetAttributes(pageOptions);
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions, Guid categoryId)
        {
            AssertCallerIsOwner();
            return await _das.GetAttributes(pageOptions, categoryId);
        }
        
        protected void AssertCallerIsOwner()
        {
            //HACK: refactoring profiles
            // if (this._context.Caller.IsOwner == false)
            // {
            //     throw new SecurityException("Caller must be owner");
            // }
        }
    }
}