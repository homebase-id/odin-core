using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Odin.Core.Identity;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests._Universal;

public class PermissionKeyTestList
{
    private List<int> _permissionKeys;

    
    public PermissionKeyTestList(params int[] pk)
    {
        _permissionKeys = pk.ToList();
    }

    public List<int> PermissionKeys
    {
        get => _permissionKeys;
        set => _permissionKeys = value;
    }
}

public class AppWriteOnlyAccessToDrive : IApiClientContext
{
    private readonly OdinId _odinId;
    private readonly PermissionKeyTestList _keys;
    private AppApiClientFactory _factory;

    public AppWriteOnlyAccessToDrive(OdinId odinId, PermissionKeyTestList keys)
    {
        _odinId = odinId;
        _keys = keys;
    }

    public async Task Initialize(OwnerApiClient ownerApiClient)
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
                        Drive = TargetDrive.NewTargetDrive(),
                        Permission = DrivePermission.Write
                    }
                }
            },
            PermissionSet = new PermissionSet(_keys.PermissionKeys ?? new List<int>())
        };

        var circles = new List<Guid>();
        var circlePermissions = new PermissionSetGrantRequest();
        await ownerApiClient.Apps.RegisterApp(appId, permissions, circles, circlePermissions);

        var (appToken, appSharedSecret) = await ownerApiClient.Apps.RegisterAppClient(appId);
        _factory = new AppApiClientFactory(appToken, appSharedSecret);
    }

    public IApiClientFactory GetFactory()
    {
        Guard.Argument(_factory, nameof(_factory)).NotNull("did you call initialize?");
        return _factory;
    }
}