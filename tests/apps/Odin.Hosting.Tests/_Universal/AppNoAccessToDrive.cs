using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.Permissions;

namespace Odin.Hosting.Tests._Universal;

public class AppPermissionKeysOnly(TestPermissionKeyList keys) : IApiClientContext
{
    private AppApiClientFactory _factory;

    public TargetDrive TargetDrive { get; }
    public DrivePermission DrivePermission { get; } = DrivePermission.None;

    public async Task Initialize(OwnerApiClientRedux ownerApiClient)
    {
        // Prepare the app
        Guid appId = Guid.NewGuid();
        var permissions = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>(),
            PermissionSet = new PermissionSet(keys.PermissionKeys)
        };

        var circles = new List<Guid>();
        var circlePermissions = new PermissionSetGrantRequest();
        await ownerApiClient.AppManager.RegisterApp(appId, permissions, circles, circlePermissions);

        var (appToken, appSharedSecret) = await ownerApiClient.AppManager.RegisterAppClient(appId);
        _factory = new AppApiClientFactory(appToken, appSharedSecret);
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
        return nameof(AppWriteOnlyAccessToDrive);
    }
}