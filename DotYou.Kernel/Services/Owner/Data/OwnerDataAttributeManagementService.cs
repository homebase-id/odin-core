using System;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Services.DataAttribute;
using DotYou.Types;
using DotYou.Types.DataAttribute;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Owner.Data
{
    /// <summary>
    /// Enables the owner of a DI to read/write their data attributes.  This handles both fixed
    /// and generic attributes.  (Fixed attributes are those built-into the system like
    /// Name, Address, etc.)
    /// </summary>
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

        public Task SaveConnectedProfile(ConnectedProfile profile)
        {
            AssertCallerIsOwner();
            return _das.SaveConnectedProfile(profile);
        }

        public Task SavePublicProfile(Profile profile)
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