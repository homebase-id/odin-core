using System;
using System.Security;
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
            var name = await _das.GetAttributeByType((int)AttributeTypes.Name, ProfileConstants.PublicProfileCategoryId);
            var profilePic = await _das.GetAttributeByType((int)AttributeTypes.ProfilePic, ProfileConstants.PublicProfileCategoryId);

            var profile = new BasicProfileInfo()
            {
                Name = (NameAttribute)name,
                Photo = (ProfilePicAttribute) profilePic
            };

            return profile;
        }

        public Task SaveConnectedProfile(params BaseAttribute[] attributes)
        {
            AssertCallerIsOwner();
            return _das.SavePublicProfile(attributes);
        }

        public async Task<BasicProfileInfo> GetBasicConnectedProfile()
        {
            var name = await _das.GetAttributeByType((int)AttributeTypes.Name, ProfileConstants.ConnectProfileCategoryId);
            var profilePic = await _das.GetAttributeByType((int)AttributeTypes.ProfilePic, ProfileConstants.ConnectProfileCategoryId);

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