using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Hosting.Tests.YouAuthApi.ApiClient.Drives;

namespace Odin.Hosting.Tests.YouAuthApi.ApiClient
{
    public class YouAuthApiClient
    {
        private readonly TestIdentity _identity;

        private readonly YouAuthDriveApiClient _driveApiClient;

        public YouAuthApiClient(TestIdentity identity, ClientAccessToken clientAccessToken)
        {
            _identity = identity;
            _driveApiClient = new YouAuthDriveApiClient(_identity, clientAccessToken);
        }

        public TestIdentity Identity => _identity;

        public YouAuthDriveApiClient Drives => _driveApiClient;

        
    }
}