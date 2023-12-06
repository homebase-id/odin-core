using System.Threading.Tasks;
using Odin.Core.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests._Universal;

public class OwnerClientContext : IApiClientContext
{
    private OwnerApiClientFactory _factory;

    public OwnerClientContext(TargetDrive targetDrive)
    {
        TargetDrive = targetDrive;
    }

    public TargetDrive TargetDrive { get; }

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