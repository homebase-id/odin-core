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

public class GuestAccess : IApiClientContext
{
    private readonly string _odinId;
    private readonly List<DriveGrantRequest> _driveGrants;
    private readonly TestPermissionKeyList _keys;
    private GuestApiClientFactory _factory;

    public GuestAccess(string odinId, List<DriveGrantRequest> driveGrants, TestPermissionKeyList keys = null)
    {
        _odinId = odinId;
        _driveGrants = driveGrants;
        _keys = keys;
    }

    public TargetDrive TargetDrive { get; } = default;

    public async Task Initialize(OwnerApiClientRedux ownerApiClient)
    {
        var domain = new AsciiDomainName(_odinId);

        var circleId = Guid.NewGuid();
        await ownerApiClient.Network.CreateCircle(circleId, "Circle with valid permissions",
            new PermissionSetGrantRequest()
            {
                Drives = _driveGrants,
                PermissionSet = new PermissionSet(_keys.PermissionKeys)
            });

        var circles = new List<GuidId>() { circleId };
        await ownerApiClient.YouAuth.RegisterDomain(domain, circles);

        var registerClientResponse = await ownerApiClient.YouAuth.RegisterClient(domain, "test scenario client");

        var cat = ClientAccessToken.FromPortableBytes(registerClientResponse.Content.Data);

        _factory = new GuestApiClientFactory(cat.ToAuthenticationToken(), cat.SharedSecret.GetKey());
    }

    public IApiClientFactory GetFactory()
    {
        return _factory;
    }

    public override string ToString()
    {
        return MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
    }
}