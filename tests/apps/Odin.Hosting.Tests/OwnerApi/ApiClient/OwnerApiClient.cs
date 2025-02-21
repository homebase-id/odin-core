using System;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Configuration;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Follower;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.CircleMembership;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Connections;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.YouAuth;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Rsa;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Security;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Transit;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Transit.Query;
using Odin.Hosting.Tests.OwnerApi.Utils;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient
{
    public class OwnerApiClient
    {
        private readonly TestIdentity _identity;
        private readonly OwnerApiTestUtils _ownerApi;

        private readonly AppsApiClient _appsApiClient;
        private readonly CircleNetworkApiClient _circleNetworkApiClient;

        private readonly CircleMembershipApiClient _circleMembershipApiClient;
        private readonly TransitApiClient _transitApiClient;
        private readonly DriveApiClient _driveApiClient;
        private readonly DriveApiClientRedux _driveApiClientRedux;
        private readonly OwnerFollowerApiClient _ownerFollowerApiClient;
        private readonly SecurityApiClient _securityApiClient;
        private readonly PublicPrivateKeyApiClient _publicPrivateKey;
        private readonly YouAuthDomainApiClient _youAuthDomainApiClient;
        private readonly OwnerConfigurationApiClient _ownerConfigurationApiClient;

        public OwnerApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity, Guid? systemApiKey = null)
        {
            _ownerApi = ownerApi;
            _identity = identity;

            _appsApiClient = new AppsApiClient(ownerApi, identity);
            _circleNetworkApiClient = new CircleNetworkApiClient(ownerApi, identity);
            _transitApiClient = new TransitApiClient(ownerApi, identity);
            _driveApiClient = new DriveApiClient(ownerApi, identity);
            _driveApiClientRedux = new DriveApiClientRedux(ownerApi, identity);
            _ownerFollowerApiClient = new OwnerFollowerApiClient(ownerApi, identity);
            _securityApiClient = new SecurityApiClient(ownerApi, identity);
            _publicPrivateKey = new PublicPrivateKeyApiClient(ownerApi, identity);
            _circleMembershipApiClient = new CircleMembershipApiClient(ownerApi, identity);
            _youAuthDomainApiClient = new YouAuthDomainApiClient(ownerApi, identity);
            _ownerConfigurationApiClient = new OwnerConfigurationApiClient(ownerApi, identity);

            TransitQuery = new OwnerTransitQueryApiClient(ownerApi, identity);
        }

        public OwnerAuthTokenContext GetTokenContext()
        {
            var t = this._ownerApi.GetOwnerAuthContext(_identity.OdinId);
            return t;
        }

        public OwnerConfigurationApiClient Configuration => _ownerConfigurationApiClient;

        public TestIdentity Identity => _identity;

        public AppsApiClient Apps => _appsApiClient;

        public SecurityApiClient Security => _securityApiClient;

        public OwnerFollowerApiClient OwnerFollower => _ownerFollowerApiClient;

        public CircleNetworkApiClient Network => _circleNetworkApiClient;

        public CircleMembershipApiClient Membership => _circleMembershipApiClient;

        public TransitApiClient Transit => _transitApiClient;

        public OwnerTransitQueryApiClient TransitQuery { get; }

        public YouAuthDomainApiClient YouAuth => _youAuthDomainApiClient;

        public PublicPrivateKeyApiClient PublicPrivateKey => _publicPrivateKey;

        public DriveApiClient Drive => _driveApiClient;

        public DriveApiClientRedux DriveRedux => _driveApiClientRedux;

        public async Task InitializeIdentity(InitialSetupRequest setupConfig)
        {
            var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);

            var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
            var initIdentityResponse = await svc.InitializeIdentity(setupConfig);
            ClassicAssert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

            var getIsIdentityConfiguredResponse = await svc.IsIdentityConfigured();
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getIsIdentityConfiguredResponse.Content);
        }
    }
}