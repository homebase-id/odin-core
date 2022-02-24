using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dawn;
using Microsoft.Extensions.Logging;
using Youverse.Core.Identity.DataAttribute;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Profile
{
    /// <inheritdoc cref="IProfileAttributeManagementService"/>
    public class ProfileAttributeManagementService : IProfileAttributeManagementService
    {
        private readonly DotYouContext _context;
        private readonly AttributeStorage _das;
        private readonly ISystemStorage _systemStorage;


        public ProfileAttributeManagementService(DotYouContext context, ILogger<IProfileAttributeManagementService> logger, ISystemStorage systemStorage)
        {
            _context = context.GetCurrent();
            _systemStorage = systemStorage;
            _das = new AttributeStorage(context, systemStorage);
        }

        public Task SavePublicProfile(NameAttribute primaryName, ProfilePicAttribute photo, params BaseAttribute[] additionalAttributes)
        {
            AssertCallerIsOwner();
            Guard.Argument(primaryName, nameof(primaryName)).NotNull();
            Guard.Argument(photo, nameof(photo)).NotNull();

            primaryName.Id = ProfileConstants.PublicProfilePrimaryNameId;
            photo.Id = ProfileConstants.PublicProfilePhotoId;

            var allAttributes = new List<BaseAttribute>(additionalAttributes ?? Array.Empty<BaseAttribute>()) {primaryName, photo};
            return _das.SaveAttributeCollection(ProfileConstants.PublicProfileAttributeCollectionId, allAttributes);
        }

        public async Task<BasicProfileInfo> GetBasicPublicProfile()
        {
            AssertCallerIsOwner();

            var subset = await _das.GetAttributeCollectionSubset(ProfileConstants.PublicProfileAttributeCollectionId,
                new[]
                {
                    ProfileConstants.PublicProfilePrimaryNameId,
                    ProfileConstants.PublicProfilePhotoId
                });

            var name = (NameAttribute) subset.Single(s => s.Id == ProfileConstants.PublicProfilePrimaryNameId);
            var profilePic = (ProfilePicAttribute) subset.Single(s => s.Id == ProfileConstants.PublicProfilePhotoId);

            var profile = new BasicProfileInfo()
            {
                Name = name,
                Photo = profilePic
            };

            return profile;
        }


        public Task SaveConnectedProfile(NameAttribute primaryName, ProfilePicAttribute photo, params BaseAttribute[] additionalAttributes)
        {
            AssertCallerIsOwner();
            Guard.Argument(primaryName, nameof(primaryName)).NotNull();
            Guard.Argument(photo, nameof(photo)).NotNull();

            primaryName.Id = ProfileConstants.ConnectedProfilePrimaryNameId;
            photo.Id = ProfileConstants.ConnectedProfilePhotoId;

            var allAttributes = new List<BaseAttribute>(additionalAttributes ?? Array.Empty<BaseAttribute>()) {primaryName, photo};
            return _das.SaveAttributeCollection(ProfileConstants.ConnectedProfileAttributeCollectionId, allAttributes);
        }

        public async Task<BasicProfileInfo> GetBasicConnectedProfile(bool fallbackToEmpty = false)
        {
            AssertCallerIsOwner();

            var subset = await _das.GetAttributeCollectionSubset(ProfileConstants.ConnectedProfileAttributeCollectionId,
                new[]
                {
                    ProfileConstants.ConnectedProfilePrimaryNameId,
                    ProfileConstants.ConnectedProfilePhotoId
                });


            var name = (NameAttribute) subset.SingleOrDefault(s => s.Id == ProfileConstants.ConnectedProfilePrimaryNameId);
            var profilePic = (ProfilePicAttribute) subset.SingleOrDefault(s => s.Id == ProfileConstants.ConnectedProfilePhotoId);

            if (name == null)
            {
                if (fallbackToEmpty)
                {
                    return BasicProfileInfo.Empty;
                }

                return null;
            }

            var profile = new BasicProfileInfo()
            {
                Name = name,
                Photo = profilePic
            };

            return profile;
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

        public async Task<PagedResult<BaseAttribute>> GetAttributeCollection(Guid id, PageOptions pageOptions)
        {
            AssertCallerIsOwner();
            return await _das.GetAttributeCollection(id, pageOptions);
        }

        public async Task SaveAttributeCollection(Guid id, params BaseAttribute[] attributes)
        {
            AssertCallerIsOwner();
            await _das.SaveAttributeCollection(id, attributes);
        }

        private void AssertCallerIsOwner()
        {
            //HACK: refactoring profiles
            // if (this._context.GetCurrent().Caller.IsOwner == false)
            // {
            //     throw new SecurityException("Caller must be owner");
            // }
        }
    }
}