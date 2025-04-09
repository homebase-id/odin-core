using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests._Universal;

public class AppReadonlyAccessToDrive //: IApiClientContext
{
    private AppApiClientFactory _factory;

    public async Task Initialize(OwnerApiClient ownerApiClient, TargetDrive targetDrive)
    {
        // Prepare the app
        Guid appId = Guid.NewGuid();
        var permissions = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = targetDrive,
                        Permission = DrivePermission.Read
                    }
                }
            }
        };

        var circles = new List<Guid>();
        var circlePermissions = new PermissionSetGrantRequest();
        await ownerApiClient.Apps.RegisterApp(appId, permissions, circles, circlePermissions);

        var (appToken, appSharedSecret) = await ownerApiClient.Apps.RegisterAppClient(appId);
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
        return System.Reflection.MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
    }
}