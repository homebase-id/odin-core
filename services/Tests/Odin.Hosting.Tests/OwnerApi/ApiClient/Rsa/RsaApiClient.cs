using System.Threading.Tasks;
using Odin.Hosting.Controllers.Anonymous.RsaKeys;
using Odin.Hosting.Tests.OwnerApi.Utils;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Rsa;

public class RsaApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public RsaApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task<GetPublicKeyResponse> GetSigningPublicKey()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRsaHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await svc.GetSigningPublicKey();

            return resp.Content;
        }
    }
    
    public async Task<GetPublicKeyResponse> GetOnlinePublicKey()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IRsaHttpClientForOwner>(client, ownerSharedSecret);
            var resp = await svc.GetOnlinePublicKey();

            return resp.Content;
        }
    }

}