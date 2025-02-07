using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Core.Util;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;

namespace Odin.Hosting.Tests._Universal;

public class GuestAccess(string odinId, List<DriveGrantRequest> driveGrants, List<GuidId> circles, TestPermissionKeyList keys = null)
    : IApiClientContext
{
    private GuestApiClientFactory _factory;

    public TargetDrive TargetDrive { get; } = default;

    public DrivePermission DrivePermission => throw new NotImplementedException();

    public async Task Initialize(OwnerApiClientRedux ownerApiClient)
    {
        var domain = new AsciiDomainName(odinId);

        var circleId = Guid.NewGuid();
        await ownerApiClient.Network.CreateCircle(circleId, "Circle with valid permissions",
            new PermissionSetGrantRequest()
            {
                Drives = driveGrants,
                PermissionSet = new PermissionSet(keys.PermissionKeys)
            });

        var circleList = new List<GuidId>() { circleId };
        circleList.AddRange(circles);
        await ownerApiClient.YouAuth.RegisterDomain(domain, circleList);

        var registerClientResponse = await ownerApiClient.YouAuth.RegisterClient(domain, "test scenario client");

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