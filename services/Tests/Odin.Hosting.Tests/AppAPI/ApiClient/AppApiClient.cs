using System;
using System.Net.Http;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Hosting.Tests.AppAPI.ApiClient.Auth;
using Odin.Hosting.Tests.AppAPI.ApiClient.Drive;
using Odin.Hosting.Tests.AppAPI.ApiClient.Security;
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
        }

        public TestIdentity Identity => _identity;

        public GuidId AccessRegistrationId => _token.ClientAuthToken.Id;

        public AppDriveApiClient Drive { get; }


        public AppSecurityApiClient Security { get; }

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