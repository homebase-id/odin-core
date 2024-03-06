using System.Threading.Tasks;
using Odin.Core.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests._Universal;

public class OwnerClientContext(TargetDrive targetDrive) : IApiClientContext
{
    private OwnerApiClientFactory _factory;

    public TargetDrive TargetDrive { get; } = targetDrive;

    public async Task Initialize(OwnerApiClientRedux ownerApiClient)
    {
        var t = ownerApiClient.GetTokenContext();
        _factory = new OwnerApiClientFactory(t.AuthenticationResult, t.SharedSecret.GetKey());
        await Task.CompletedTask;
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