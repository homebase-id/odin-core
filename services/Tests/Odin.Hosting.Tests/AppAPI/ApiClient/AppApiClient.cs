using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core.Storage;
using Odin.Hosting.Tests.AppAPI.ApiClient.Auth;
using Odin.Hosting.Tests.AppAPI.ApiClient.Drive;
using Odin.Hosting.Tests.AppAPI.ApiClient.Follower;
using Odin.Hosting.Tests.AppAPI.ApiClient.Membership.CircleMembership;
using Odin.Hosting.Tests.AppAPI.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.AppAPI.ApiClient.Membership.Connections;
using Odin.Hosting.Tests.AppAPI.ApiClient.Security;
using Odin.Hosting.Tests.AppAPI.ApiClient.TransitQuery;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.AppAPI.ApiClient
{
    public class AppApiClient
    {
        private readonly TestIdentity _identity;
        private readonly AppApiTestUtils _appApiTestUtils;

        private readonly AppClientToken _token;

        public AppApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity, Guid appId)
        {
            _identity = identity;
            _appApiTestUtils = new AppApiTestUtils(ownerApi);

            var o = new OwnerApiClient(ownerApi, identity);
            var p = o.Apps.RegisterAppClient(appId).GetAwaiter().GetResult();

            _token = new AppClientToken()
            {
                OdinId = identity.OdinId,
                ClientAuthToken = p.clientAuthToken,
                SharedSecret = p.sharedSecret
            };

            this.Drive = new AppDriveApiClient(ownerApi, _token);
            this.Security = new AppSecurityApiClient(ownerApi, _token);
            this.Follower = new AppFollowerApiClient(ownerApi, _token);
            this.CircleMembership = new AppCircleMembershipApiClient(ownerApi, _token);
            this.CircleDefinitions = new AppCircleDefinitionApiClient(ownerApi, _token);
            this.CircleNetwork = new AppCircleNetworkApiClient(ownerApi, _token);
            this.CircleNetworkRequests = new AppCircleNetworkRequestsApiClient(ownerApi, _token);
            this.TransitQuery = new AppTransitQueryApiClient(ownerApi, _token);
            this.TransitReactionSender = new AppTransitReactionSenderApiClient(ownerApi, _token);
        }

        public AppCircleNetworkRequestsApiClient CircleNetworkRequests { get; }

        public AppCircleNetworkApiClient CircleNetwork { get; }

        public TestIdentity Identity => _identity;

        public GuidId AccessRegistrationId => _token.ClientAuthToken.Id;

        public AppDriveApiClient Drive { get; }

        public AppFollowerApiClient Follower { get; }

        public AppSecurityApiClient Security { get; }

        public AppCircleMembershipApiClient CircleMembership { get; }

        public AppCircleDefinitionApiClient CircleDefinitions { get; }

        public AppTransitQueryApiClient TransitQuery { get; }

        public AppTransitReactionSenderApiClient TransitReactionSender { get; }

        public async Task Logout()
        {
            var client = this.CreateAppApiHttpClient();
            {
                var authService = RefitCreator.RestServiceFor<IAppAuthenticationClient>(client, this._token.SharedSecret);
                await authService.Logout();
            }
        }

        public async Task<ApiResponse<HttpContent>> PreAuth()
        {
            var client = this.CreateAppApiHttpClient();
            var authService = RefitCreator.RestServiceFor<IAppAuthenticationClient>(client, this._token.SharedSecret);
            var result = await authService.PreAuthWebsocket();
            return result;
        }

        private HttpClient CreateAppApiHttpClient(FileSystemType fileSystemType = FileSystemType.Standard)
        {
            return _appApiTestUtils.CreateAppApiHttpClient(_token, fileSystemType);
        }
    }
}