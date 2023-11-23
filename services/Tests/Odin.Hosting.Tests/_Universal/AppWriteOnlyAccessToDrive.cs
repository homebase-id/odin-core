using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dawn;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests._Universal;

public class AppWriteOnlyAccessToDrive : IScenarioContext
{
    private AppApiClientFactory _factory;
    public const string Key = nameof(AppWriteOnlyAccessToDrive);

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
                        Permission = DrivePermission.Write
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
        Guard.Argument(_factory, nameof(_factory)).NotNull("did you call initialize?");
        return _factory;
    }
}