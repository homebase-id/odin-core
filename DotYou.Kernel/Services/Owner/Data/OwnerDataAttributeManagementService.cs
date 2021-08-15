using System;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types;
using DotYou.Types.DataAttribute;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Owner.Data
{
    /// <summary>
    /// Profile information 
    /// </summary>
    public class OwnerProfile
    {
        /// <summary>
        /// 
        /// </summary>
        public Guid Id { get; set; }

        public NameAttribute Name { get; set; }
        public ProfilePicAttribute Photo { get; set; }
    }

    /// <inheritdoc cref="IOwnerDataAttributeManagementService"/>
    public class OwnerDataAttributeManagementService : DotYouServiceBase, IOwnerDataAttributeManagementService
    {
        private readonly OwnerDataAttributeStorage _das;

        public OwnerDataAttributeManagementService(DotYouContext context, ILogger logger) : base(context, logger, null, null)
        {
            _das = new OwnerDataAttributeStorage(context, logger);
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
    }
}