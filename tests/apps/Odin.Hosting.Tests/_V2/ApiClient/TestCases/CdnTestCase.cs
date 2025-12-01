using System;
using System.Reflection;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._V2.ApiClient.Factory;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._V2.ApiClient.TestCases;

public class CdnTestCase(TargetDrive targetDrive, DrivePermission drivePermission) : IApiClientContext
{
    private ApiClientFactoryV2 _factory;
    private static ClientAuthenticationToken AuthenticationToken { get; }

    public TargetDrive TargetDrive { get; } = targetDrive;
    public DrivePermission DrivePermission { get; } = drivePermission;

    static CdnTestCase()
    {
        AuthenticationToken = new ClientAuthenticationToken
        {
            Id = Guid.Parse("058de171-2525-45dc-b496-8eafb85a703b"),
            AccessTokenHalfKey = Guid.Parse("41a247d8-fba0-442f-8391-05df4391a4e0").ToByteArray().ToSensitiveByteArray(),
            ClientTokenType = ClientTokenType.Cdn
        };
    }

    public async Task Initialize(OwnerApiClientRedux ownerApiClient)
    {
        _factory = new ApiClientFactoryV2(YouAuthDefaults.XTokenCookieName, AuthenticationToken, secret: null);
        await Task.CompletedTask;
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

    public static string GetAuthToken64()
    {
        return AuthenticationToken.ToPortableBytes64();
    }
}