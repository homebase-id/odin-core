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

    /// <summary>
    /// The fixed bearer token both the V1 fixture and the V2 in-process framework's
    /// <c>CdnSession</c> use. Both frameworks need the SAME value because the host's
    /// <c>Cdn__RequiredAuthToken</c> config is derived from this single field; changing the GUIDs
    /// here automatically moves both sides in lockstep.
    /// </summary>
    public static ClientAuthenticationToken AuthenticationToken { get; }

    public TargetDrive TargetDrive { get; } = targetDrive;
    public DrivePermission DrivePermission { get; } = drivePermission;
    public Guid DriveId { get; } = targetDrive.Alias;

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
        var name= System.Reflection.MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        return $"{name} with drive:{DrivePermission}";
    }

    public static string GetAuthToken64()
    {
        return AuthenticationToken.ToPortableBytes64();
    }
}