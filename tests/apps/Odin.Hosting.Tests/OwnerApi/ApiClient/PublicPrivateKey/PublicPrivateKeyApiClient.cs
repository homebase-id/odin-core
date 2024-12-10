using System.Threading.Tasks;
using Odin.Services.EncryptionKeyService;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Tests.OwnerApi.ApiClient.PublicPrivateKey;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.Rsa;

public class PublicPrivateKeyApiClient
{
    private readonly TestIdentity _identity;
    private readonly OwnerApiTestUtils _ownerApi;

    public PublicPrivateKeyApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
    {
        _ownerApi = ownerApi;
        _identity = identity;
    }

    public async Task<GetPublicKeyResponse> GetSigningPublicKey()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RestService.For<IPublicPrivateKeyHttpClientForOwner>(client);
            var resp = await svc.GetSigningPublicKey();

            return resp.Content;
        }
    }
    
    public async Task<GetPublicKeyResponse> GetOnlinePublicKey()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RestService.For<IPublicPrivateKeyHttpClientForOwner>(client);
            var resp = await svc.GetOnlinePublicKey();
            return resp.Content;
        }
    }
    
    public async Task<GetEccPublicKeyResponse> GetEccOnlinePublicKey()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RestService.For<IPublicPrivateKeyHttpClientForOwner>(client);
            var resp = await svc.GetEccOnlinePublicKey();
            return resp.Content;
        }
    }
    
    public async Task<string> GetEccOfflinePublicKey()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RestService.For<IPublicPrivateKeyHttpClientForOwner>(client);
            var resp = await svc.GetEccOfflinePublicKey();
            return resp.Content;
        }
    }
    
    public async Task<GetPublicKeyResponse> GetOfflinePublicKey()
    {
        var client = _ownerApi.CreateOwnerApiHttpClient(_identity, out var ownerSharedSecret);
        {
            var svc = RestService.For<IPublicPrivateKeyHttpClientForOwner>(client);
            var resp = await svc.GetOfflinePublicKey();
            return resp.Content;
        }
    }

}