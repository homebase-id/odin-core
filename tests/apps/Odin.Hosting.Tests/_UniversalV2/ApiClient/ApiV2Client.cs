using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;

namespace Odin.Hosting.Tests._UniversalV2.ApiClient
{
    public class ApiV2Client(IApiClientFactory factory, OdinId identity)
    {
        public OdinId Identity => identity;
        
        public UniversalDriveApiClient Drive { get; } = new(identity, factory);
    }
}