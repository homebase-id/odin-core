using System;
using System.Threading.Tasks;
using Dawn;
using DotYou.IdentityRegistry;
using DotYou.Types.Identity;
using Identity.DataType.Attributes;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Admin.IdentityManagement
{
    public class AdminAdminIdentityAttributeService : DotYouServiceBase, IAdminIdentityAttributeService
    {
        private const string ADMIN_IDENTITY_COLLECTION = "AdminIdentity";
        private readonly Guid NAME_ATTRIBUTE_ID = Guid.Parse("ff06ce6d-d871-4a82-9775-071aa70fdab4");
        
        public AdminAdminIdentityAttributeService(DotYouContext context, ILogger logger) : base(context, logger)
        {
        }
        
        public async Task<NameAttribute> GetPrimaryName()
        {
            //Note: the ID for the primary name is a fixed attribute in the system
            var name = await WithTenantStorageReturnSingle<NameAttribute>(ADMIN_IDENTITY_COLLECTION, s=>s.Get(NAME_ATTRIBUTE_ID));
            return name;
        }

        public Task SavePrimaryName(NameAttribute name)
        {
            Guard.Argument(name, nameof(name)).NotNull();
            Guard.Argument(name.Personal, nameof(name.Personal)).NotEmpty();
            Guard.Argument(name.Surname, nameof(name.Surname)).NotEmpty();
                
            //Note: the ID for the primary name is a fixed attribute in the system
            name.Id = NAME_ATTRIBUTE_ID;
            WithTenantStorage<NameAttribute>(ADMIN_IDENTITY_COLLECTION, s=>s.Save(name));
            return Task.CompletedTask;
        }
    }
}