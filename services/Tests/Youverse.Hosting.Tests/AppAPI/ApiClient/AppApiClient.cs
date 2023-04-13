using System;
using System.Net.Http;
using System.Threading.Tasks;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Storage;
using Youverse.Hosting.Tests.AppAPI.ApiClient.Auth;
using Youverse.Hosting.Tests.AppAPI.Utils;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests.AppAPI.ApiClient
{
    public class AppApiClient
    {
        private readonly TestIdentity _identity;
        private readonly OwnerApiTestUtils _ownerApi;
        private readonly AppApiTestUtils _appApiTestUtils;
        private readonly ClientAuthenticationToken _clientAuthToken;
        private readonly byte[] _sharedSecret;

        public AppApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity, Guid appId)
        {
            _ownerApi = ownerApi;
            _identity = identity;
            _appApiTestUtils = new AppApiTestUtils(_ownerApi);

            var o = new OwnerApiClient(ownerApi, identity);
            var p = o.Apps.RegisterAppClient(appId).GetAwaiter().GetResult();
            this._clientAuthToken = p.clientAuthToken;
            this._sharedSecret = p.sharedSecret;
        }

        public TestIdentity Identity => _identity;
        public GuidId AccessRegistrationId => _clientAuthToken.Id;

        public HttpClient CreateAppApiHttpClient(FileSystemType fileSystemType = FileSystemType.Standard)
        {
            return _appApiTestUtils.CreateAppApiHttpClient(this._identity.OdinId, _clientAuthToken, _sharedSecret, fileSystemType);
        }
        
        public async Task Logout()
        {
            using (var client = this.CreateAppApiHttpClient())
            {
                var authService = RefitCreator.RestServiceFor<IAppAuthenticationClient>(client, this._sharedSecret);
                await authService.Logout();
            }
        }
    }
}