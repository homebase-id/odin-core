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
        UseReviewedSecurityTier = false
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

    public bool ConnectedIdentitiesCanCommentOnAnonymousDrives { get; set; }

    /// <summary>
    /// When true, a connected caller's security tier is decided by whether the owner has reviewed them:
    /// reviewed callers stay at <see cref="SecurityGroupType.Connected"/>, unreviewed ones drop to
    /// <see cref="SecurityGroupType.Authenticated"/>.  Off by default, which is today's behaviour --
    /// every connected caller is Connected regardless of review.
    /// </summary>
    /// <remarks>
    /// A dark-launch switch, not a feature the owner is meant to reason about.  Turning it on tightens
    /// access: content behind a <c>connected</c> ACL stops being readable by connections the owner never
    /// reviewed, which is the point of the recut but is a real reduction for anyone relying on today's
    /// looser behaviour.  Reversible by turning it off; nothing is written or migrated either way.
    /// <para>
    /// Ignored on a tenant that has not yet run the v15 -&gt; v16 upgrade, because that is the pass which
    /// fills in <c>ReviewedAt</c> from prior Confirmed-circle membership.  Honouring it earlier would read
    /// every connection as unreviewed and demote the lot in one go.
    /// </para>
    /// </remarks>
    public bool UseReviewedSecurityTier { get; set; }

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