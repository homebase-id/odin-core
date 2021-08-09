using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Dawn;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Services.DataAttribute;
using DotYou.Types;
using DotYou.Types.DataAttribute;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Owner.IdentityManagement
{
    /// <summary>
    /// Specialized storage for DI Profiles.  This does not enforce data
    /// security.  That should be done by the consuming services.
    /// </summary>
    internal sealed class DataAttributeStorage : DotYouServiceBase
    {
        private const string ADMIN_IDENTITY_COLLECTION = "AdminIdentity";
        private const string PUBLIC_INFO_COLLECTION = "PublicInfo";
        private const string CONNECTED_INFO_COLLECTION = "PublicInfo";

        private const string CATEGORY_ATTRIBUTE_STORAGE = "cdas";
        private const string ATTRIBUTE_STORAGE = "das";

        private readonly Guid NAME_ATTRIBUTE_ID = Guid.Parse("ff06ce6d-d871-4a82-9775-071aa70fdab4");
        private readonly Guid PUBLIC_PROFILE_ID = Guid.Parse("ffffff6d-d8ff-4aff-97ff-071aafffdfff");
        private readonly Guid CONNECTED_PROFILE_ID = Guid.Parse("EEEEEf6d-d8ff-4aff-97ff-071aafffdfff");

        public DataAttributeStorage(DotYouContext context, ILogger logger) : base(context, logger, null, null)
        {
        }

        public async Task<NameAttribute> GetPrimaryName()
        {
            //Note: the ID for the primary name is a fixed attribute in the system
            var name = await WithTenantStorageReturnSingle<NameAttribute>(ADMIN_IDENTITY_COLLECTION, s => s.Get(NAME_ATTRIBUTE_ID));
            return name;
        }

        public Task SavePrimaryName(NameAttribute name)
        {
            Guard.Argument(name, nameof(name)).NotNull();
            Guard.Argument(name.Personal, nameof(name.Personal)).NotEmpty();
            Guard.Argument(name.Surname, nameof(name.Surname)).NotEmpty();

            //Note: the ID for the primary name is a fixed attribute in the system
            name.Id = NAME_ATTRIBUTE_ID;
            WithTenantStorage<NameAttribute>(ADMIN_IDENTITY_COLLECTION, s => s.Save(name));
            return Task.CompletedTask;
        }
        
        public Task SaveConnectedProfile(ConnectedProfile profile)
        {
            //HACK: I Used a full object here with static id as I'm focused on the ui.  the storage needs to be redesigned
            Guard.Argument(profile, nameof(profile)).NotNull();
            profile.Id = CONNECTED_PROFILE_ID;
            WithTenantStorage<Profile>(CONNECTED_INFO_COLLECTION, s => s.Save(profile));
            return Task.CompletedTask;
        }

        public Task SavePublicProfile(Profile profile)
        {
            //HACK: I Used a full object here with static id as I'm focused on the ui.  the storage needs to be redesigned
            Guard.Argument(profile, nameof(profile)).NotNull();
            profile.Id = PUBLIC_PROFILE_ID;
            WithTenantStorage<Profile>(PUBLIC_INFO_COLLECTION, s => s.Save(profile));
            return Task.CompletedTask;
        }

        public async Task<Profile> GetConnectedProfile()
        {
            return await WithTenantStorageReturnSingle<Profile>(CONNECTED_INFO_COLLECTION, s => s.Get(CONNECTED_PROFILE_ID));
        }

        public async Task<Profile> GetPublicProfile()
        {
            return await WithTenantStorageReturnSingle<Profile>(PUBLIC_INFO_COLLECTION, s => s.Get(PUBLIC_PROFILE_ID));
        }
        
        public async Task<PagedResult<DataAttributeCategory>> GetCategories(PageOptions pageOptions)
        {
            AssertCallerIsOwner();
            var results = await WithTenantStorageReturnList<DataAttributeCategory>(CATEGORY_ATTRIBUTE_STORAGE, s => s.GetList(pageOptions));
            return results;
        }

        public Task SaveCategory(DataAttributeCategory category)
        {
            WithTenantStorage<DataAttributeCategory>(CATEGORY_ATTRIBUTE_STORAGE, s => s.Save(category));
            return Task.CompletedTask;
        }

        public Task DeleteCategory(Guid id)
        {
            WithTenantStorage<DataAttributeCategory>(CATEGORY_ATTRIBUTE_STORAGE, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public Task SaveAttribute(BaseAttribute attribute)
        {
            WithTenantStorage<BaseAttribute>(ATTRIBUTE_STORAGE, s => s.Save(attribute));
            return Task.CompletedTask;
        }

        public Task DeleteAttribute(Guid id)
        {
            WithTenantStorage<DataAttributeCategory>(ATTRIBUTE_STORAGE, s => s.Delete(id));
            return Task.CompletedTask;
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => true;
            var results = await WithTenantStorageReturnList<BaseAttribute>(ATTRIBUTE_STORAGE, s => s.Find(predicate, pageOptions));
            return results;
        }

        public async Task<PagedResult<BaseAttribute>> GetAttributes(PageOptions pageOptions, Guid categoryId)
        {
            Expression<Func<BaseAttribute, bool>> predicate = attr => attr.CategoryId == categoryId;
            var results = await WithTenantStorageReturnList<BaseAttribute>(ATTRIBUTE_STORAGE, s => s.Find(predicate, pageOptions));
            return results;
        }

    }
}