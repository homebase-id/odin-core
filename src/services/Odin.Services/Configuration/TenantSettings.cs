using System;
using System.Collections.Generic;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;

namespace Odin.Services.Configuration;

public class TenantSettings
{
    public static readonly Guid ConfigKey = Guid.Parse("32078bbb-8773-4371-b9bc-78d83c680e13");

    public static TenantSettings Default { get; } = new TenantSettings()
    {
        AnonymousVisitorsCanViewConnections = false,
        AuthenticatedIdentitiesCanViewConnections = false,
        AllConnectedIdentitiesCanViewConnections = true,
        AnonymousVisitorsCanViewWhoIFollow = false,
        AuthenticatedIdentitiesCanViewWhoIFollow = false,
        AllConnectedIdentitiesCanViewWhoIFollow = false,
        AuthenticatedIdentitiesCanCommentOnAnonymousDrives = false,
        AuthenticatedIdentitiesCanReactOnAnonymousDrives = true,
        ConnectedIdentitiesCanReactOnAnonymousDrives = true,
        ConnectedIdentitiesCanCommentOnAnonymousDrives = true,
        DisableAutoAcceptIntroductionsForTests = false,
        DisableAutoAcceptConnectionRequests = false,
        SendMonthlySecurityHealthReport = false,
        HideOwnerCirclesFromApps = false,
        DisableAllowIntroductions = false
    };

    /// <summary/>
    public bool AnonymousVisitorsCanViewWhoIFollow { get; set; }

    public bool AuthenticatedIdentitiesCanViewWhoIFollow { get; set; }

    /// <summary/>
    public bool AllConnectedIdentitiesCanViewWhoIFollow { get; set; }

    /// <summary/>
    public bool AnonymousVisitorsCanViewConnections { get; set; }

    /// <summary/>
    public bool AuthenticatedIdentitiesCanViewConnections { get; set; }

    /// <summary/>
    public bool AllConnectedIdentitiesCanViewConnections { get; set; }

    public bool AuthenticatedIdentitiesCanReactOnAnonymousDrives { get; set; }

    public bool AuthenticatedIdentitiesCanCommentOnAnonymousDrives { get; set; }

    public bool ConnectedIdentitiesCanReactOnAnonymousDrives { get; set; }
    
    public bool DisableAutoAcceptIntroductionsForTests { get; set; }
    
    public bool SendMonthlySecurityHealthReport { get; set; }
    
    public bool DisableAutoAcceptIntroductions { get; set; }

    /// <summary>
    /// When true, incoming connection requests with origin <see cref="Membership.Connections.Requests.ConnectionRequestOrigin.IdentityOwnerApp"/>
    /// are NOT auto-accepted; they land in the pending list for manual acceptance.
    /// Defaults to true so existing identities preserve current (manual-accept) behavior.
    /// </summary>
    public bool DisableAutoAcceptConnectionRequests { get; set; } = false;

    /// <summary>
    /// When true, nobody may introduce the owner to anyone: every incoming introduction is refused, and the
    /// introduction preflight reports it as not permitted.  When false (the default) any connection may
    /// introduce, reviewed or not.  Replaces the
    /// <see cref="PermissionKeys.AllowIntroductions"/> circle permission as the check that decides it.
    /// </summary>
    public bool DisableAllowIntroductions { get; set; }

    public bool ConnectedIdentitiesCanCommentOnAnonymousDrives { get; set; }


    /// <summary>
    /// When true, an app listing circles is shown only circles that belong to an app; circles with no
    /// owning app (the owner's own, and the system circles) are left out.  Off by default, which is
    /// today's behaviour -- an app sees every circle it has permission to read.
    /// </summary>
    /// <remarks>
    /// A dark-launch switch.  The filter keeps what an app can offer in step with what the review path lets
    /// it do (an app cannot enrol anyone into a circle no app owns), but it also changes every existing
    /// app screen that lists circles, so it is enabled one tenant at a time.  The owner console is never
    /// filtered.  Reversible by turning it off; nothing is written either way.
    /// </remarks>
    public bool HideOwnerCirclesFromApps { get; set; }

    public List<int> GetAdditionalPermissionKeysForAuthenticatedIdentities()
    {
        List<int> permissionKeys = new List<int>();
        if (this.AuthenticatedIdentitiesCanViewConnections)
        {
            permissionKeys.Add(PermissionKeys.ReadConnections);
        }

        if (this.AuthenticatedIdentitiesCanViewWhoIFollow)
        {
            permissionKeys.Add(PermissionKeys.ReadWhoIFollow);
        }

        return permissionKeys;
    }

    public DrivePermission GetAnonymousDrivePermissionsForAuthenticatedIdentities()
    {
        DrivePermission anonymousDrivePermission = DrivePermission.Read;
        if (this.AuthenticatedIdentitiesCanCommentOnAnonymousDrives)
        {
            anonymousDrivePermission |= DrivePermission.Comment;
        }

        if (this.AuthenticatedIdentitiesCanReactOnAnonymousDrives)
        {
            anonymousDrivePermission |= DrivePermission.React;
        }

        return anonymousDrivePermission;
    }
    
    public List<int> GetAdditionalPermissionKeysForConnectedIdentities()
    {
        List<int> permissionKeys = new List<int>();
        if (this.AllConnectedIdentitiesCanViewConnections)
        {
            permissionKeys.Add(PermissionKeys.ReadConnections);
        }

        if (this.AllConnectedIdentitiesCanViewWhoIFollow)
        {
            permissionKeys.Add(PermissionKeys.ReadWhoIFollow);
        }
        
        return permissionKeys;
    }

    public DrivePermission GetAnonymousDrivePermissionsForConnectedIdentities()
    {
        DrivePermission anonymousDrivePermission = DrivePermission.Read;
        if (this.ConnectedIdentitiesCanCommentOnAnonymousDrives)
        {
            anonymousDrivePermission |= DrivePermission.Comment;
        }

        if (this.ConnectedIdentitiesCanReactOnAnonymousDrives)
        {
            anonymousDrivePermission |= DrivePermission.React;
        }

        return anonymousDrivePermission;
    }
}