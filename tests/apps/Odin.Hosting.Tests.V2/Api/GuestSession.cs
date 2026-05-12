using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Util;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// A Guest (YouAuth) caller bound to one identity. Built via <see cref="SetupAsync"/>, which uses
/// an owner session to create a circle with the given drive permissions, register a YouAuth domain
/// granted that circle, and register a client under the domain — yielding a token + shared secret
/// equivalent to a connected peer browsing in.
/// </summary>
public sealed class GuestSession : IV2Caller
{
    public OdinId Identity { get; }
    public AsciiDomainName GuestDomain { get; }
    public InProcessApiClientFactory Factory { get; }
    public AuthV2Client Auth { get; }

    private GuestSession(
        OdinHost host,
        OdinId identity,
        AsciiDomainName guestDomain,
        ClientAuthenticationToken token,
        byte[] sharedSecret)
    {
        Identity = identity;
        GuestDomain = guestDomain;
        Factory = new InProcessApiClientFactory(host, YouAuthDefaults.XTokenCookieName, token, sharedSecret.ToSensitiveByteArray());
        Auth = new AuthV2Client(Identity, Factory);
    }

    public static async Task<GuestSession> SetupAsync(
        OwnerSession owner,
        OdinHost host,
        TargetDrive targetDrive,
        DrivePermission drivePermission)
    {
        var domain = new AsciiDomainName($"{Guid.NewGuid():n}-test.org");

        var circleId = Guid.NewGuid();
        var circleResp = await owner.Admin.CreateCircle(circleId, "Circle with valid permissions",
            new PermissionSetGrantRequest
            {
                Drives = new List<DriveGrantRequest>
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive
                        {
                            Drive = targetDrive,
                            Permission = drivePermission
                        }
                    }
                },
                PermissionSet = default!
            });
        if (!circleResp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"CreateCircle failed: {circleResp.StatusCode}");
        }

        await owner.Admin.RegisterYouAuthDomain(domain, [circleId]);
        var clientReg = await owner.Admin.RegisterYouAuthClient(domain);

        var cat = ClientAccessToken.FromPortableBytes(clientReg.Content!.Data);
        return new GuestSession(host, owner.Identity, domain, cat.ToAuthenticationToken(), cat.SharedSecret.GetKey());
    }
}
