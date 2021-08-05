using System;
using System.Threading.Tasks;
using Dawn;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Services.Circle;
using DotYou.Types;
using DotYou.Types.Identity;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Admin.IdentityManagement
{
    public class AdminIdentityAttributeService : DotYouServiceBase, IAdminIdentityAttributeService
    {
        private const string ADMIN_IDENTITY_COLLECTION = "AdminIdentity";
        private const string PUBLIC_INFO_COLLECTION = "PublicInfo";
        private const string CONNECTED_INFO_COLLECTION = "PublicInfo";
        private readonly Guid NAME_ATTRIBUTE_ID = Guid.Parse("ff06ce6d-d871-4a82-9775-071aa70fdab4");

        private readonly Guid PUBLIC_PROFILE_ID = Guid.Parse("ffffff6d-d8ff-4aff-97ff-071aafffdfff");
        private readonly Guid CONNECTED_PROFILE_ID = Guid.Parse("EEEEEf6d-d8ff-4aff-97ff-071aafffdfff");

        private readonly ICircleNetworkService _circleNetwork;

        public AdminIdentityAttributeService(DotYouContext context, ICircleNetworkService circleNetwork, ILogger logger) : base(context, logger, null, null)
        {
            _circleNetwork = circleNetwork;
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

        public async Task<Profile> GetMyProfile()
        {
            var isConnectedWithMe = await _circleNetwork.GetSystemCircle(base.Context.CallerDotYouId) == SystemCircle.Connected;
            if (isConnectedWithMe)
            {
                //HACK: I Used a full object here with static id as I'm focused on the ui.  the storage needs to be redesigned
                var connectedProfile = await WithTenantStorageReturnSingle<ConnectedProfile>(CONNECTED_INFO_COLLECTION, s => s.Get(CONNECTED_PROFILE_ID));
                connectedProfile.SystemCircle = SystemCircle.Connected;
                return connectedProfile;
            }

            //HACK: I Used a full object here with static id as I'm focused on the ui.  the storage needs to be redesigned
            var profile = await WithTenantStorageReturnSingle<Profile>(PUBLIC_INFO_COLLECTION, s => s.Get(PUBLIC_PROFILE_ID));
            profile.SystemCircle = SystemCircle.PublicAnonymous;
            return profile;
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
    }
}