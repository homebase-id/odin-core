using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Util;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests._Universal;

public class GuestDomainReadonlyAccessToDrive : IScenarioContext
{
    private GuestApiClientFactory _factory;

    public async Task Initialize(OwnerApiClient ownerApiClient, TargetDrive targetDrive)
    {
        var domain = new AsciiDomainName("test.org");

        var circle1 = await ownerApiClient.Membership.CreateCircle("Circle with valid permissions",
            new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new ()
                    {
                        PermissionedDrive = new ()
                        {
                            Drive = targetDrive,
                            Permission = DrivePermission.Read
                        }
                    }
                },
                PermissionSet = default
            });

        var circles = new List<GuidId>() { circle1.Id };
        await ownerApiClient.YouAuth.RegisterDomain(domain, circles);

        var registerClientResponse = await ownerApiClient.YouAuth.RegisterClient(domain, "test scenario client");

        var cat = ClientAccessToken.FromPortableBytes(registerClientResponse.Content.Data);

        _factory = new GuestApiClientFactory(cat.ToAuthenticationToken(), cat.SharedSecret.GetKey());
    }

    public IApiClientFactory GetFactory()
    {
        return _factory;
    }
}