using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Services.Configuration;
using Youverse.Hosting.Tests.OwnerApi.ApiClient.Cron;
using Youverse.Hosting.Tests.OwnerApi.ApiClient.Transit;
using Youverse.Hosting.Tests.OwnerApi.Configuration;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.OwnerApi.ApiClient
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
        }

        public TestIdentity Identity => _identity;

        public AppsApiClient Apps => _appsApiClient;

        public CronApiClient Cron => _cronApiClient;
        public FollowerApiClient Follower => _followerApiClient;
        public CircleNetworkApiClient Network => _circleNetworkApiClient;

        public TransitApiClient Transit => _transitApiClient;

        public DriveApiClient Drive => _driveApiClient;

        public async Task InitializeIdentity(InitialSetupRequest setupConfig)
        {
            using (var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);
                var initIdentityResponse = await svc.InitializeIdentity(setupConfig);
                Assert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

                var getIsIdentityConfiguredResponse = await svc.IsIdentityConfigured();
                Assert.IsTrue(getIsIdentityConfiguredResponse.IsSuccessStatusCode);
                Assert.IsTrue(getIsIdentityConfiguredResponse.Content);
            }
        }
    }
}