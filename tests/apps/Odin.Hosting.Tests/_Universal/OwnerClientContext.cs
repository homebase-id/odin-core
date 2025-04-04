using System;
using System.Threading.Tasks;
using Odin.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;

namespace Odin.Hosting.Tests._Universal;

public class OwnerClientContext(TargetDrive targetDrive) : IApiClientContext
{
    private OwnerApiClientFactory _factory;

    public TargetDrive TargetDrive { get; } = targetDrive;
    public DrivePermission DrivePermission => DrivePermission.All;

    public Task Initialize(OwnerApiClientRedux ownerApiClient)
    {
        var t = ownerApiClient.GetTokenContext();
        _factory = new OwnerApiClientFactory(t.AuthenticationResult, t.SharedSecret.GetKey());
        return Task.CompletedTask;
    }

    public IApiClientFactory GetFactory()
    {
        return _factory;
    }
    
    public Task Cleanup()
    {
        //no-op
        return Task.CompletedTask;
    }
    
    public override string ToString()
    {
        return System.Reflection.MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
    }
}