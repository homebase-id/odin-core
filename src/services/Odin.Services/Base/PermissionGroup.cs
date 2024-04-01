#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Serilog;

namespace Odin.Services.Base;

/// <summary>
/// Specifies a set of permissions.  This allows an identity's permissions to come from multiple sources such as circles.
/// </summary>
public class PermissionGroup(
    PermissionSet permissionSet,
    IEnumerable<DriveGrant>? driveGrants,
    SensitiveByteArray? keyStoreKey,
    SymmetricKeyEncryptedAes? encryptedIcrKey)
{
    public bool HasDrivePermission(Guid driveId, DrivePermission permission)
    {
        // var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);
        // return grant != null && grant.PermissionedDrive.Permission.HasFlag(permission);

        if (null == driveGrants)
        {
            return false;
        }

        var hasPermission = driveGrants.Any(g => g.DriveId == driveId && g.PermissionedDrive.Permission.HasFlag(permission));
        return hasPermission;
    }

    public bool HasPermission(int permission)
    {
        return permissionSet?.HasKey(permission) ?? false;
    }

    /// <summary>
    /// Returns the encryption key specific to this app.  This is only available
    /// when the owner is making an HttpRequest.
    /// </summary>
    /// <returns></returns>
    public Guid? GetDriveId(TargetDrive drive)
    {
        var grant = driveGrants?.FirstOrDefault(g => g.PermissionedDrive.Drive == drive);
        return grant?.DriveId;
    }

    /// <summary>
    /// Returns the encryption key specific to this app.  This is only available
    /// when the owner is making an HttpRequest.
    /// </summary>
    /// <returns></returns>
    public SensitiveByteArray? GetDriveStorageKey(Guid driveId, out int grantsCount)
    {
        grantsCount = 0;
        var grants = driveGrants?.Where(g => g.DriveId == driveId).ToList();

        if (grants == null)
        {
            return null;
        }

        grantsCount = grants.Count();

        // var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);
        // var grant = grants?.FirstOrDefault();
        // if (null == grant)
        // {
        //     return null;
        // }

        foreach (var grant in grants)
        {
            //If we cannot decrypt the storage key BUT the caller has access to the drive,
            //this most likely denotes an anonymous drive.  Return an empty key which means encryption will fail
            if (keyStoreKey == null || grant.KeyStoreKeyEncryptedStorageKey == null)
            {
                Log.Debug(
                    "Grant for drive {permissionDrive} with permission value ({permission}) has null key store key:{kskNull} and null key store key encrypted storage key: {kskstoragekey}",
                    grant.PermissionedDrive.Drive, grant.PermissionedDrive.Permission, keyStoreKey == null, grant.KeyStoreKeyEncryptedStorageKey == null);

                // return null;
                continue;
            }

            var key = keyStoreKey;
            try
            {
                var storageKey = grant.KeyStoreKeyEncryptedStorageKey.DecryptKeyClone(key);

                Log.Information(
                    "Grant for drive {permissionDrive} with permission value ({permission}) returned the storage key",
                    grant.PermissionedDrive.Drive, grant.PermissionedDrive.Permission);
                
                return storageKey;
            }
            catch
            {
                Log.Warning("Failed tyring to decrypt storage key for drive {pd} ", grant.PermissionedDrive.Drive);
            }
        }

        return null;
    }

    public TargetDrive? GetTargetDrive(Guid driveId)
    {
        var grant = driveGrants?.FirstOrDefault(g => g.DriveId == driveId);
        return grant?.PermissionedDrive.Drive;
    }

    public SensitiveByteArray? GetIcrKey()
    {
        var key = keyStoreKey;
        return encryptedIcrKey?.DecryptKeyClone(key);
    }

    public RedactedPermissionGroup Redacted()
    {
        if (null == permissionSet)
        {
            return new RedactedPermissionGroup()
            {
                PermissionSet = new RedactedPermissionSet(),
                DriveGrants = new List<RedactedDriveGrant>()
            };
        }

        return new RedactedPermissionGroup()
        {
            PermissionSet = permissionSet == null ? new PermissionSet().Redacted() : permissionSet.Redacted(),
            DriveGrants = driveGrants?.Select(r => r.Redacted()) ?? new List<RedactedDriveGrant>()
        };
    }
}

public class RedactedPermissionGroup
{
    public RedactedPermissionGroup()
    {
        this.DriveGrants = new List<RedactedDriveGrant>();
        this.PermissionSet = new RedactedPermissionSet();
    }

    public IEnumerable<RedactedDriveGrant> DriveGrants { get; set; }
    public RedactedPermissionSet PermissionSet { get; set; }
}