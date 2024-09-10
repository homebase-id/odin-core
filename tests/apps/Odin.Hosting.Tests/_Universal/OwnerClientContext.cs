using System.Threading.Tasks;
using Odin.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._UniversalV2.Factory;

namespace Odin.Hosting.Tests._Universal;

public class OwnerClientContext(TargetDrive targetDrive) : IApiClientContext
{
    private IApiClientFactory _factory;

    public TargetDrive TargetDrive { get; } = targetDrive;
    public DrivePermission DrivePermission => DrivePermission.All;

    public async Task Initialize(OwnerApiClientRedux ownerApiClient)
    {
        var t = ownerApiClient.GetTokenContext();
        _factory = new OwnerApiClientFactory(t.AuthenticationToken, t.SharedSecret.GetKey());
        await Task.CompletedTask;
    }

    public Task InitializeV2(OwnerAuthTokenContext tokenContext)
    {
        _factory = new OwnerApiClientFactoryV2(tokenContext.AuthenticationToken, tokenContext.SharedSecret.GetKey());
        return Task.CompletedTask;
    }
    
    public IApiClientFactory GetFactory()
    {
        return _factory;
    }
    
    public override string ToString()
    {
        return nameof(OwnerClientContext);
    }
}