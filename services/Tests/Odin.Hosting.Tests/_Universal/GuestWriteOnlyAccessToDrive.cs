using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Dawn;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Util;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests._Universal;

public class GuestWriteOnlyAccessToDrive : IApiClientContext
{
    private readonly TestPermissionKeyList _keys;
    private GuestApiClientFactory _factory;

    public GuestWriteOnlyAccessToDrive(TargetDrive targetDrive, TestPermissionKeyList keys = null)
    {
        TargetDrive = targetDrive;
        _keys = keys;
    }

    public TargetDrive TargetDrive { get; }

    public async Task Initialize(OwnerApiClient ownerApiClient)
    {
        var domain = new AsciiDomainName("test.org");

        var circle1 = await ownerApiClient.Membership.CreateCircle("Circle with valid permissions",
            new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new()
                        {
                            Drive = TargetDrive,
                            Permission = DrivePermission.Write
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
        Guard.Argument(_factory, nameof(_factory)).NotNull("did you call initialize?");
        return _factory;
    }

    public override string ToString()
    {
        return MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
    }
}