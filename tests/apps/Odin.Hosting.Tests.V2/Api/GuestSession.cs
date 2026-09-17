using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Util;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.Guest;
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
    public DriveHandles Drives { get; }
    public V1Handles V1 { get; }

    private GuestSession(
        OdinHost host,
        OdinId identity,
        AsciiDomainName guestDomain,
        ClientAuthenticationToken token,
        byte[] sharedSecret)
    {
        Identity = identity;
        GuestDomain = guestDomain;
        Factory = new InProcessApiClientFactory(host, YouAuthDefaults.XTokenCookieName, token,
            sharedSecret.ToSensitiveByteArray(), GuestApiPathConstantsV1.BasePathV1);
        Auth = new AuthV2Client(Identity, Factory);
        Drives = new DriveHandles(Identity, Factory);
        V1 = new V1Handles(Identity, Factory);
    }

    /// <summary>
    /// The common case: a throwaway guest domain granted one drive permission.
    /// </summary>
    public static Task<GuestSession> SetupAsync(
        OwnerSession owner,
        TargetDrive targetDrive,
        DrivePermission drivePermission)
        => SetupAsync(owner, new PermissionSetGrantRequest
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

    /// <summary>
    /// The general form, mirroring <see cref="AppSession.SetupAsync(OwnerSession, PermissionSetGrantRequest, string)"/>:
    /// the caller supplies the whole grant, so a guest can hold permission keys, several drives, or
    /// keys with no drive at all — none of which the drive-scoped overload can express.
    /// </summary>
    /// <param name="domain">
    /// Omit for a throwaway domain. Pass one to model "this identity browsing in as a guest", which
    /// is what the collaboration-channel fixtures need — the author's own identity is the domain.
    /// </param>
    /// <remarks>
    /// Porting note: V1's <c>GuestSpecifyAccessToDrive</c> accepts a <c>TestPermissionKeyList</c> and
    /// then silently drops it (its <c>_keys</c> field is never read and the circle is built with
    /// <c>PermissionSet = default</c>). A port that faithfully passes those keys through here would be
    /// *granting more* than the original did — a coverage change wearing a port's clothes. Keep the
    /// keys off when porting a fixture that used that context.
    /// </remarks>
    public static async Task<GuestSession> SetupAsync(
        OwnerSession owner,
        PermissionSetGrantRequest grant,
        AsciiDomainName? domain = null)
    {
        var guestDomain = domain ?? NewGuestDomain();

        var circleId = Guid.NewGuid();
        var circleResp = await owner.Admin.CreateCircle(circleId, "Circle with valid permissions", grant);
        if (!circleResp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"CreateCircle failed: {circleResp.StatusCode}");
        }

        await owner.Admin.RegisterYouAuthDomain(guestDomain, [circleId]);
        var clientReg = await owner.Admin.RegisterYouAuthClient(guestDomain);

        var cat = ClientAccessToken.FromPortableBytes(clientReg.Content!.Data);
        return new GuestSession(owner.Host, owner.Identity, guestDomain, cat.ToAuthenticationToken(), cat.SharedSecret.GetKey());
    }

    private static AsciiDomainName NewGuestDomain() =>
        new($"{Guid.NewGuid():n}-test.org");
}
