using System;
using System.Threading.Tasks;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._V2.ApiClient.Factory;
using Odin.Services.Authentication.Owner;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._V2.ApiClient.TestCases;

public class OwnerTestCase(TargetDrive targetDrive) : IApiClientContext
{
    private ApiClientFactoryV2 _factory;

    public TargetDrive TargetDrive { get; } = targetDrive;
    public DrivePermission DrivePermission => DrivePermission.All;
    public Guid DriveId { get; } = targetDrive.Alias;

    public Task Initialize(OwnerApiClientRedux ownerApiClient)
    {
        var t = ownerApiClient.GetTokenContext();
        _factory = new ApiClientFactoryV2(OwnerAuthConstants.CookieName, t.AuthenticationResult, t.SharedSecret.GetKey());
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
        var name= System.Reflection.MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        return $"{name} with drive:{DrivePermission}";
    }
}