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

public class AppSpecifyDriveAccess(TargetDrive targetDrive, DrivePermission permission, TestPermissionKeyList keys = null)
    : IApiClientContext
{
    private AppApiClientFactory _factory;

    public Task InitializeV2(OwnerAuthTokenContext tokenContext)
    {
        throw new NotImplementedException();
    }

    public TargetDrive TargetDrive { get; } = targetDrive;
    public DrivePermission DrivePermission { get; } = permission;

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
                        Permission = DrivePermission
                    }
                }
            },
            PermissionSet = new PermissionSet(keys?.PermissionKeys ?? new List<int>())
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