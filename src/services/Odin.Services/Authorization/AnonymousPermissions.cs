using System.Collections.Generic;
using System.Linq;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;

namespace Odin.Services.Authorization;

/// <summary>
/// What a visitor with no token may do: read every drive that allows anonymous reads, read transit, and see
/// the connections and who the owner follows when the owner's settings open them.
/// </summary>
public static class AnonymousPermissions
{
    public static CallerContext Caller() => new(odinId: null, securityLevel: SecurityGroupType.Anonymous, masterKey: null);

    public static PermissionContext Create(IEnumerable<StorageDrive> anonymousDrives, TenantSettings settings)
    {
        var anonDriveGrants = anonymousDrives.Select(d => new DriveGrant()
        {
            DriveId = d.Id,
            PermissionedDrive = new PermissionedDrive()
            {
                Drive = d.TargetDriveInfo,
                Permission = DrivePermission.Read
            }
        }).ToList();

        var anonPerms = new List<int>();

        if (settings.AnonymousVisitorsCanViewConnections)
        {
            anonPerms.Add(PermissionKeys.ReadConnections);
        }

        if (settings.AnonymousVisitorsCanViewWhoIFollow)
        {
            anonPerms.Add(PermissionKeys.ReadWhoIFollow);
        }

        anonPerms.Add(PermissionKeys.UseTransitRead);

        var permissionGroupMap = new Dictionary<string, PermissionGroup>
        {
            { "read_anonymous_drives", new PermissionGroup(new PermissionSet(anonPerms), anonDriveGrants, null, null) },
        };

        return new PermissionContext(permissionGroupMap, sharedSecretKey: null);
    }
}
