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

    /// <summary>
    /// Makes <paramref name="odinContext"/> an anonymous visitor's: its caller and its permissions
    /// </summary>
    public static void Apply(IOdinContext odinContext, IEnumerable<StorageDrive> anonymousDrives, TenantSettings settings)
    {
        odinContext.Caller = Caller();
        odinContext.SetPermissionContext(Create(anonymousDrives, settings));
    }

    /// <param name="useTransitRead">False only for a link preview, which has never been given it</param>
    public static PermissionContext Create(IEnumerable<StorageDrive> anonymousDrives, TenantSettings settings,
        bool useTransitRead = true)
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

        if (useTransitRead)
        {
            anonPerms.Add(PermissionKeys.UseTransitRead);
        }

        var permissionGroupMap = new Dictionary<string, PermissionGroup>
        {
            { "read_anonymous_drives", new PermissionGroup(new PermissionSet(anonPerms), anonDriveGrants, null, null) },
        };

        // Built with the public-drive grants, so a public drive's reads skip its grant (PermissionContext.ReadsPublicDrives)
        return new PermissionContext(permissionGroupMap, sharedSecretKey: null, readsPublicDrives: true);
    }
}
