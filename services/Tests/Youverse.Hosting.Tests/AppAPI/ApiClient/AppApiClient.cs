using System;
using System.Net.Http;
using System.Threading.Tasks;
using Youverse.Core;
using Youverse.Core.Storage;
using Youverse.Hosting.Tests.AppAPI.ApiClient.Auth;
using Youverse.Hosting.Tests.AppAPI.ApiClient.Drive;
using Youverse.Hosting.Tests.AppAPI.Utils;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.AppAPI.ApiClient
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
        }

        public TestIdentity Identity => _identity;
        
        public GuidId AccessRegistrationId => _token.ClientAuthToken.Id;

        public AppDriveApiClient Drive { get; }

        public async Task Logout()
        {
            using (var client = this.CreateAppApiHttpClient())
            {
                var authService = RefitCreator.RestServiceFor<IAppAuthenticationClient>(client, this._token.SharedSecret);
                await authService.Logout();
            }
        }

        private HttpClient CreateAppApiHttpClient(FileSystemType fileSystemType = FileSystemType.Standard)
        {
            return _appApiTestUtils.CreateAppApiHttpClient(_token, fileSystemType);
        }
    }
}