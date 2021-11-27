using System;
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
    internal sealed class OwnerDataAttributeStorage<T>
    {
        private const string ADMIN_IDENTITY_COLLECTION = "AdminIdentity";
        private const string PUBLIC_INFO_COLLECTION = "PublicInfo";
        private const string CONNECTED_INFO_COLLECTION = "PublicInfo";

        private const string CATEGORY_ATTRIBUTE_STORAGE = "cdas";
        private const string ATTRIBUTE_STORAGE = "das";

        private readonly Guid NAME_ATTRIBUTE_ID = Guid.Parse("ff06ce6d-d871-4a82-9775-071aa70fdab4");
        private readonly Guid PUBLIC_PROFILE_ID = Guid.Parse("ffffff6d-d8ff-4aff-97ff-071aafffdfff");
        private readonly Guid CONNECTED_PROFILE_ID = Guid.Parse("EEEEEf6d-d8ff-4aff-97ff-071aafffdfff");
        
        private readonly DotYouContext _context;
        private readonly ISystemStorage _systemStorage;

        public OwnerDataAttributeStorage(DotYouContext context, ILogger<T> logger, ISystemStorage systemStorage)
        {
            _context = context;
            _systemStorage = systemStorage;
        }

        public async Task<NameAttribute> GetPrimaryName()
        {
            //Note: the ID for the primary name is a fixed attribute in the system
            var name = await _systemStorage.WithTenantSystemStorageReturnSingle<NameAttribute>(ADMIN_IDENTITY_COLLECTION, s => s.Get(NAME_ATTRIBUTE_ID));
            return name;
        }

        public Task SavePrimaryName(NameAttribute name)
        {
            Guard.Argument(name, nameof(name)).NotNull();
            Guard.Argument(name.Personal, nameof(name.Personal)).NotEmpty();
            Guard.Argument(name.Surname, nameof(name.Surname)).NotEmpty();

            //Note: the ID for the primary name is a fixed attribute in the system
            name.Id = NAME_ATTRIBUTE_ID;
            _systemStorage.WithTenantSystemStorage<NameAttribute>(ADMIN_IDENTITY_COLLECTION, s => s.Save(name));
            return Task.CompletedTask;
        }

        public Task SaveConnectedProfile(OwnerProfile profile)
        {
            //HACK: I Used a full object here with static id as I'm focused on the ui.  the storage needs to be redesigned
            Guard.Argument(profile, nameof(profile)).NotNull();
            profile.Id = CONNECTED_PROFILE_ID;
            _systemStorage.WithTenantSystemStorage<OwnerProfile>(CONNECTED_INFO_COLLECTION, s => s.Save(profile));
            return Task.CompletedTask;
        }

        public Task SavePublicProfile(OwnerProfile profile)
        {
            //HACK: I Used a full object here with static id as I'm focused on the ui.  the storage needs to be redesigned
            Guard.Argument(profile, nameof(profile)).NotNull();
            profile.Id = PUBLIC_PROFILE_ID;
            _systemStorage.WithTenantSystemStorage<OwnerProfile>(PUBLIC_INFO_COLLECTION, s => s.Save(profile));
            return Task.CompletedTask;
        }

        public async Task<OwnerProfile> GetConnectedProfile()
        {
            return await _systemStorage.WithTenantSystemStorageReturnSingle<OwnerProfile>(CONNECTED_INFO_COLLECTION, s => s.Get(CONNECTED_PROFILE_ID));
        }

        public async Task<OwnerProfile> GetPublicProfile()
        {
            return await _systemStorage.WithTenantSystemStorageReturnSingle<OwnerProfile>(PUBLIC_INFO_COLLECTION, s => s.Get(PUBLIC_PROFILE_ID));
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
        
        protected void AssertCallerIsOwner()
        {
            if (this._context.Caller.IsOwner == false)
            {
                throw new SecurityException("Caller must be owner");
            }
        }
    }
}