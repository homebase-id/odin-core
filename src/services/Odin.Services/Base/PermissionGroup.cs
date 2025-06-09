#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Serilog;

namespace Odin.Services.Base;

/// <summary>
/// Specifies a set of permissions.  This allows an identity's permissions to come from multiple sources such as circles.
/// </summary>
public class PermissionGroup : IGenericCloneable<PermissionGroup>
{
    private readonly PermissionSet? _permissionSet;
    private readonly IEnumerable<DriveGrant>? _driveGrants;
    private readonly SensitiveByteArray? _keyStoreKey;
    private readonly SymmetricKeyEncryptedAes? _encryptedIcrKey;

    public PermissionGroup(PermissionSet permissionSet, IEnumerable<DriveGrant>? driveGrants, SensitiveByteArray? keyStoreKey,
        SymmetricKeyEncryptedAes? encryptedIcrKey)
    {
        _permissionSet = permissionSet;
        _driveGrants = driveGrants;
        _keyStoreKey = keyStoreKey;
        _encryptedIcrKey = encryptedIcrKey;
    }

    public PermissionGroup(PermissionGroup other)
    {
        _permissionSet = other._permissionSet?.Clone();
        _driveGrants = other._driveGrants?.Select(dg => dg.Clone());
        _keyStoreKey = other._keyStoreKey?.Clone();
        _encryptedIcrKey = other._encryptedIcrKey?.Clone();
    }

    internal int DriveGrantCount => this._driveGrants?.Count() ?? 0;

    public PermissionGroup Clone()
    {
        return new PermissionGroup(this);
    }

    public bool HasDrivePermission(Guid driveId, DrivePermission permission)
    {
        if (null == _driveGrants)
        {
            return false;
        }

        var hasPermission = _driveGrants.Any(g => g.DriveId == driveId && g.PermissionedDrive.Permission.HasFlag(permission));
        return hasPermission;
    }

    public bool HasPermission(int permission)
    {
        return this._permissionSet?.HasKey(permission) ?? false;
    }

    /// <summary>
    /// Returns the encryption key specific to this app.  This is only available
    /// when the owner is making an HttpRequest.
    /// </summary>
    /// <returns></returns>
    public SensitiveByteArray? GetDriveStorageKey(Guid driveId, out int grantsCount)
    {
        grantsCount = 0;
        var grants = _driveGrants?.Where(g => g.DriveId == driveId).ToList();

        if (grants == null)
        {
            return null;
        }

        grantsCount = grants.Count();

        foreach (var grant in grants)
        {
            //If we cannot decrypt the storage key BUT the caller has access to the drive,
            //this most likely denotes an anonymous drive.  Return an empty key which means encryption will fail
            if (this._keyStoreKey == null || grant.KeyStoreKeyEncryptedStorageKey == null)
            {
                Log.Verbose(
                    "Grant for drive {permissionDrive} with permission value ({permission}) has null key store key:{kskNull} and null key store key encrypted storage key: {kskstoragekey}",
                    grant.PermissionedDrive.Drive, grant.PermissionedDrive.Permission, this._keyStoreKey == null,
                    grant.KeyStoreKeyEncryptedStorageKey == null);

                // return null;
                continue;
            }

            var key = this._keyStoreKey;
            try
            {
                var storageKey = grant.KeyStoreKeyEncryptedStorageKey.DecryptKeyClone(key);

                Log.Verbose(
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
        var grant = _driveGrants?.FirstOrDefault(g => g.DriveId == driveId);
        return grant?.PermissionedDrive.Drive;
    }

    public SensitiveByteArray? GetIcrKey()
    {
        var key = this._keyStoreKey;
        return _encryptedIcrKey?.DecryptKeyClone(key);
    }

    public SensitiveByteArray? GetKeyStoreKey()
    {
        return this._keyStoreKey;
    }

    public RedactedPermissionGroup Redacted()
    {
        if (null == _permissionSet)
        {
            return new RedactedPermissionGroup()
            {
                PermissionSet = new RedactedPermissionSet(),
                DriveGrants = new List<RedactedDriveGrant>()
            };
        }

        return new RedactedPermissionGroup()
        {
            PermissionSet = _permissionSet == null ? new PermissionSet().Redacted() : _permissionSet.Redacted(),
            DriveGrants = _driveGrants?.Select(r => r.Redacted()) ?? new List<RedactedDriveGrant>()
        };
    }
}

public class RedactedPermissionGroup
{
    public IEnumerable<RedactedDriveGrant> DriveGrants { get; set; } = new List<RedactedDriveGrant>();
    public RedactedPermissionSet PermissionSet { get; set; } = new();
}