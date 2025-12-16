using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Core.Util;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;

namespace Odin.Hosting.Tests._Universal;

public class GuestSpecifyAccessToDrive(TargetDrive targetDrive, DrivePermission drivePermission, TestPermissionKeyList keys = null) : IApiClientContext
{
    private readonly TestPermissionKeyList _keys = keys;
    private GuestApiClientFactory _factory;

    public TargetDrive TargetDrive { get; } = targetDrive;
    public DrivePermission DrivePermission { get; } = drivePermission;
    public Guid DriveId => TargetDrive.Alias;
    public async Task Initialize(OwnerApiClientRedux ownerApiClient)
    {
        var domain = new AsciiDomainName($"{Guid.NewGuid():n}-test.org");

        var circleId = Guid.NewGuid();
        await ownerApiClient.Network.CreateCircle(circleId, "Circle with valid permissions",
            new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new()
                        {
                            Drive = TargetDrive,
                            Permission = DrivePermission
                        }
                    }
                },
                PermissionSet = default
            });


        var registerResponse = await ownerApiClient.YouAuth.RegisterDomain(domain, [circleId]);
        if (!registerResponse.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to initialize scenario; Register domain returned status code: {registerResponse.StatusCode}");
        }

        var registerClientResponse = await ownerApiClient.YouAuth.RegisterClient(domain, "test scenario client");

        if (!registerClientResponse.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to initialize scenario; Register client returned status code: {registerClientResponse.StatusCode}");
        }

        var cat = ClientAccessToken.FromPortableBytes(registerClientResponse.Content.Data);

        _factory = new GuestApiClientFactory(cat.ToAuthenticationToken(), cat.SharedSecret.GetKey());
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
        return MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
    }
}