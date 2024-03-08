using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;

namespace Odin.Hosting.Tests._Universal;

public class AppSpecifyDriveAccess : IApiClientContext
{
    private readonly DrivePermission _permission;
    private readonly TestPermissionKeyList _keys;
    private AppApiClientFactory _factory;

    public AppSpecifyDriveAccess(TargetDrive targetDrive, DrivePermission permission, TestPermissionKeyList keys = null)
    {
        TargetDrive = targetDrive;
        _permission = permission;
        _keys = keys;
    }

    public TargetDrive TargetDrive { get; }

    public async Task Initialize(OwnerApiClientRedux ownerApiClient)
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
                        Drive = TargetDrive,
                        Permission = _permission
                    }
                }
            },
            PermissionSet = new PermissionSet(_keys?.PermissionKeys ?? new List<int>())
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

    public override string ToString()
    {
        return nameof(AppWriteOnlyAccessToDrive);
    }
}