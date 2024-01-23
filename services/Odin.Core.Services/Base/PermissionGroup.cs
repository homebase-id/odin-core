#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Cryptography.Data;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Drives;

namespace Odin.Core.Services.Base;

/// <summary>
/// Specifies a set of permissions.  This allows an identity's permissions to come from multiple sources such as circles.
/// </summary>
public class PermissionGroup
{
    private readonly PermissionSet _permissionSet;
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

    public bool HasDrivePermission(Guid driveId, DrivePermission permission)
    {
        var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);
        return grant != null && grant.PermissionedDrive.Permission.HasFlag(permission);
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
    public Guid? GetDriveId(TargetDrive drive)
    {
        var grant = _driveGrants?.FirstOrDefault(g => g.PermissionedDrive.Drive == drive);
        return grant?.DriveId;
    }

    /// <summary>
    /// Returns the encryption key specific to this app.  This is only available
    /// when the owner is making an HttpRequest.
    /// </summary>
    /// <returns></returns>
    public SensitiveByteArray? GetDriveStorageKey(Guid driveId)
    {
        var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);

        if (null == grant)
        {
            return null;
        }

        //If we cannot decrypt the storage key BUT the caller has access to the drive,
        //this most likely denotes an anonymous drive.  Return an empty key which means encryption will fail
        if (this._keyStoreKey == null || grant.KeyStoreKeyEncryptedStorageKey == null)
        {
            // return Array.Empty<byte>().ToSensitiveByteArray();
            return null;
        }

        var key = this._keyStoreKey;
        var storageKey = grant.KeyStoreKeyEncryptedStorageKey.DecryptKeyClone(key);
        return storageKey;
    }

    public TargetDrive? GetTargetDrive(Guid driveId)
    {
        var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);
        return grant?.PermissionedDrive.Drive;
    }

    public SensitiveByteArray? GetIcrKey()
    {
        var key = this._keyStoreKey;
        return _encryptedIcrKey?.DecryptKeyClone(key);
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
    public RedactedPermissionGroup()
    {
        this.DriveGrants = new List<RedactedDriveGrant>();
        this.PermissionSet = new RedactedPermissionSet();
    }

    public IEnumerable<RedactedDriveGrant> DriveGrants { get; set; }
    public RedactedPermissionSet PermissionSet { get; set; }
}