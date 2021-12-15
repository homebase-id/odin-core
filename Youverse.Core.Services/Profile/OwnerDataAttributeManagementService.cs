using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity.DataAttribute;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Profile
{
    /// <inheritdoc cref="IOwnerDataAttributeManagementService"/>
    public class OwnerDataAttributeManagementService : IOwnerDataAttributeManagementService
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

        public Task SavePublicProfile(params BaseAttribute[] attributes)
        {
            AssertCallerIsOwner();
            return _das.SavePublicProfile(attributes);
        }

        public async Task<BasicProfileInfo> GetBasicPublicProfile()
        {
            AssertCallerIsOwner();

            var name = await _das.GetPublicProfileAttributeByType((int)AttributeTypes.Name, ProfileConstants.PublicProfileCategoryId);
            var profilePic = await _das.GetPublicProfileAttributeByType((int)AttributeTypes.ProfilePic, ProfileConstants.PublicProfileCategoryId);
            
            if (null == name)
            {
                return null;
            }
            
            var profile = new BasicProfileInfo()
            {
                Name = (NameAttribute)name,
                Photo = (ProfilePicAttribute) profilePic
            };

            return profile;
        }
        
        public async Task<PagedResult<BaseAttribute>> GetPublicProfileAttributeCollection(PageOptions pageOptions)
        {
            AssertCallerIsOwner();
            return await _das.GetPublicProfile(pageOptions);
        }

        public Task SaveConnectedProfile(params BaseAttribute[] attributes)
        {
            AssertCallerIsOwner();
            return _das.SaveConnectedProfile(attributes);
        }

        public async Task<BasicProfileInfo> GetBasicConnectedProfile()
        {
            AssertCallerIsOwner();

            var name = await _das.GetConnectedProfileAttributeByType((int)AttributeTypes.Name, ProfileConstants.ConnectedProfileCategoryId);
            var profilePic = await _das.GetConnectedProfileAttributeByType((int)AttributeTypes.ProfilePic, ProfileConstants.ConnectedProfileCategoryId);

            if (null == name)
            {
                return null;
            }
            
            var profile = new BasicProfileInfo()
            {
                Name = (NameAttribute)name,
                Photo = (ProfilePicAttribute) profilePic
            };

            return profile;
        }
        
        public Task<PagedResult<BaseAttribute>> GetConnectedProfileAttributeCollection(PageOptions pageOptions)
        {
            AssertCallerIsOwner();
            throw new NotImplementedException();
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

        private void AssertCallerIsOwner()
        {
            //HACK: refactoring profiles
            // if (this._context.Caller.IsOwner == false)
            // {
            //     throw new SecurityException("Caller must be owner");
            // }
        }
    }
}