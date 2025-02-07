using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Core.Util;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.Permissions;

namespace Odin.Hosting.Tests._Universal;

public class ConnectedIdentityLoggedInOnGuestApi(OdinId identity, TestPermissionKeyList keys) : IApiClientContext
{
    private GuestApiClientFactory _factory;

    public TargetDrive TargetDrive { get; }
    public DrivePermission DrivePermission { get; }

    private OwnerApiClientRedux _api;

    public async Task Initialize(OwnerApiClientRedux ownerApiClient)
    {
        this._api = ownerApiClient;

        var circleId = Guid.NewGuid();
        await ownerApiClient.Network.CreateCircle(circleId, "Circle with valid permissions",
            new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(keys.PermissionKeys)
            });

        var registerResponse = await ownerApiClient.YouAuth.RegisterDomain(identity, [circleId]);
        if (!registerResponse.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to initialize scenario; Register domain ({identity}) returned status code: {registerResponse.StatusCode}");
        }

        var registerClientResponse = await ownerApiClient.YouAuth.RegisterClient(identity, "test scenario client");

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

    public async Task Cleanup()
    {
        if (null != this._api)
        {
            await this._api.YouAuth.DeleteDomainRegistration(identity);
        }
    }

    public override string ToString()
    {
        return MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
    }
}