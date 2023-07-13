using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Configuration;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Cron;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Rsa;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Transit;
using Odin.Hosting.Tests.OwnerApi.Configuration;
using Odin.Hosting.Tests.OwnerApi.Utils;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient
{
    public class OwnerApiClient
    {
        private readonly TestIdentity _identity;
        private readonly OwnerApiTestUtils _ownerApi;

        private readonly AppsApiClient _appsApiClient;
        private readonly CircleNetworkApiClient _circleNetworkApiClient;
        private readonly TransitApiClient _transitApiClient;
        private readonly DriveApiClient _driveApiClient;
        private readonly FollowerApiClient _followerApiClient;
        private readonly CronApiClient _cronApiClient;
        private readonly SecurityApiClient _securityApiClient;
        private readonly PublicPrivateKeyApiClient _publicPrivateKey;

        public OwnerApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
        {
            _ownerApi = ownerApi;
            _identity = identity;

            _appsApiClient = new AppsApiClient(ownerApi, identity);
            _circleNetworkApiClient = new CircleNetworkApiClient(ownerApi, identity);
            _transitApiClient = new TransitApiClient(ownerApi, identity);
            _driveApiClient = new DriveApiClient(ownerApi, identity);
            _followerApiClient = new FollowerApiClient(ownerApi, identity);
            _cronApiClient = new CronApiClient(ownerApi, identity);
            _securityApiClient = new SecurityApiClient(ownerApi, identity);
            _publicPrivateKey = new PublicPrivateKeyApiClient(ownerApi, identity);
        }

        public TestIdentity Identity => _identity;

        public AppsApiClient Apps => _appsApiClient;

        public SecurityApiClient Security => _securityApiClient;

        public CronApiClient Cron => _cronApiClient;

        public FollowerApiClient Follower => _followerApiClient;
        public CircleNetworkApiClient Network => _circleNetworkApiClient;

        public TransitApiClient Transit => _transitApiClient;


        public PublicPrivateKeyApiClient PublicPrivateKey => _publicPrivateKey;

        public DriveApiClient Drive => _driveApiClient;

        public async Task InitializeIdentity(InitialSetupRequest setupConfig)
        {
            var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);

            var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);
            var initIdentityResponse = await svc.InitializeIdentity(setupConfig);
            Assert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

            var getIsIdentityConfiguredResponse = await svc.IsIdentityConfigured();
            Assert.IsTrue(getIsIdentityConfiguredResponse.IsSuccessStatusCode);
            Assert.IsTrue(getIsIdentityConfiguredResponse.Content);
        }
    }
}