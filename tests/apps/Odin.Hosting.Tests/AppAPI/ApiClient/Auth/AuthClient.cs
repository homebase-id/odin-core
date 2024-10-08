using Odin.Hosting.Tests.OwnerApi.Utils;

namespace Odin.Hosting.Tests.AppAPI.ApiClient.Auth;

public class AuthClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public AuthClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }
    
}