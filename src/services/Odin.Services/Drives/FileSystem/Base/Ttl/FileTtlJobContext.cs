#nullable enable

using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Drives.FileSystem.Base.Ttl;

/// <summary>
/// Shared data and context construction for the two TTL jobs. Both run outside any HTTP request, so
/// they build a tenant-scoped system context — <see cref="PermissionContext"/> with
/// <c>isSystem: true</c> short-circuits every drive permission check
/// (<see cref="PermissionContext.HasDrivePermission"/>), which is what lets the job delete a file
/// nobody is signed in to delete.
/// </summary>
public class FileTtlJobData
{
    public OdinId? Tenant { get; set; }
    public Guid DriveId { get; set; }
    public Guid FileId { get; set; }
    public FileSystemType FileSystemType { get; set; }
}

public static class FileTtlJobContext
{
    /// <summary>
    /// The drive grant is not redundant with <c>isSystem</c>. isSystem short-circuits the permission
    /// *checks*, but the delete path also builds a client file header on its way to publishing the
    /// deleted-file notification, and that resolves the drive through
    /// <c>PermissionContext.GetTargetDrive</c> - which walks the grants and throws when it finds none.
    /// </summary>
    public static IOdinContext BuildSystemContext(OdinId tenant, TargetDrive drive)
    {
        var odinContext = new OdinContext
        {
            Tenant = tenant,
            AuthTokenCreated = null,
            Caller = new CallerContext(
                odinId: (OdinId)"system.domain",
                masterKey: null,
                securityLevel: SecurityGroupType.System,
                circleIds: null,
                tokenType: ClientTokenType.Other)
        };

        var driveGrants = new List<DriveGrant>
        {
            new()
            {
                DriveId = drive.Alias,
                PermissionedDrive = new PermissionedDrive
                {
                    Drive = drive,
                    Permission = DrivePermission.ReadWrite
                },
                KeyStoreKeyEncryptedStorageKey = null
            }
        };

        var permissionGroups = new Dictionary<string, PermissionGroup>
        {
            { "file-ttl-job", new PermissionGroup(new PermissionSet(), driveGrants, null, null) }
        };

        odinContext.SetPermissionContext(new PermissionContext(permissionGroups, null, true));
        return odinContext;
    }
}
