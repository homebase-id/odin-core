using System;
using System.Collections.Generic;
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
        SendMonthlySecurityHealthReport = false
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

    public bool ConnectedIdentitiesCanCommentOnAnonymousDrives { get; set; }

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