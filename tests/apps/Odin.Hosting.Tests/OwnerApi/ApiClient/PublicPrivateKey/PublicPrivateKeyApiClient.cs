using System.Threading.Tasks;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Odin.Services.EncryptionKeyService;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.ApiClient.PublicPrivateKey;

public class PublicPrivateKeyApiClient(OwnerApiTestUtils ownerApi, TestIdentity identity)
{
    public async Task<GetPublicKeyResponse> GetSigningPublicKey()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RestService.For<IPublicPrivateKeyHttpClientForOwner>(client);
            var resp = await svc.GetSigningPublicKey();

            return resp.Content;
        }
    }
    
    public async Task<GetPublicKeyResponse> GetOnlinePublicKey()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RestService.For<IPublicPrivateKeyHttpClientForOwner>(client);
            var resp = await svc.GetOnlinePublicKey();
            return resp.Content;
        }
    }
    
    public async Task<GetEccPublicKeyResponse> GetEccOnlinePublicKey()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RestService.For<IPublicPrivateKeyHttpClientForOwner>(client);
            var resp = await svc.GetEccOnlinePublicKey();
            return resp.Content;
        }
    }
    
    public async Task<string> GetEccOfflinePublicKey()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RestService.For<IPublicPrivateKeyHttpClientForOwner>(client);
            var resp = await svc.GetEccOfflinePublicKey();
            return resp.Content;
        }
    }
    
    public async Task<GetPublicKeyResponse> GetOfflinePublicKey()
    {
        var client = ownerApi.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
        {
            var svc = RestService.For<IPublicPrivateKeyHttpClientForOwner>(client);
            var resp = await svc.GetOfflinePublicKey();
            return resp.Content;
        }
    }

}