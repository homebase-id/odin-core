#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Hosting;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>
/// An App caller bound to one identity. Built via <see cref="SetupAsync"/>, which uses an owner
/// session to register the app + an app client and then bundles the resulting token + shared secret
/// behind the same V2 client wrappers exposed by <see cref="OwnerSession"/>.
/// </summary>
public sealed class AppSession : IV2Caller
{
    public OdinId Identity { get; }
    public Guid AppId { get; }
    public InProcessApiClientFactory Factory { get; }
    public AuthV2Client Auth { get; }

    private AppSession(OdinHost host, OdinId identity, Guid appId, Services.Authorization.ExchangeGrants.ClientAuthenticationToken token, byte[] sharedSecret)
    {
        Identity = identity;
        AppId = appId;
        Factory = new InProcessApiClientFactory(host, YouAuthConstants.AppCookieName, token, sharedSecret.ToSensitiveByteArray());
        Auth = new AuthV2Client(Identity, Factory);
    }

    /// <summary>
    /// Registers a fresh app + app client against the given <paramref name="owner"/>, granting the
    /// app the requested <paramref name="drivePermission"/> on <paramref name="targetDrive"/>.
    /// Caller is responsible for having already created the drive.
    /// </summary>
    public static async Task<AppSession> SetupAsync(
        OwnerSession owner,
        OdinHost host,
        TargetDrive targetDrive,
        DrivePermission drivePermission,
        IReadOnlyList<int>? permissionKeys = null)
    {
        var appId = Guid.NewGuid();
        var permissions = new PermissionSetGrantRequest
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
            PermissionSet = new PermissionSet(permissionKeys is null ? new List<int>() : new List<int>(permissionKeys))
        };

        await owner.Admin.RegisterApp(appId, permissions, authorizedCircles: new List<Guid>(),
            circleMemberGrantRequest: new PermissionSetGrantRequest());
        var (token, sharedSecret) = await owner.Admin.RegisterAppClient(appId);

        return new AppSession(host, owner.Identity, appId, token, sharedSecret);
    }
}
