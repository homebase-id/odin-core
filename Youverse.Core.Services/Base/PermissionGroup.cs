using System;
using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Base;

/// <summary>
/// Specifies a set of permissions.  This allows an identity's permissions to come from multiple sources such as circles.
/// </summary>
public class PermissionGroup
{
    private readonly PermissionSet _permissionSet;
    private readonly IEnumerable<DriveGrant> _driveGrants;
    private readonly SensitiveByteArray _driveDecryptionKey;

    public PermissionGroup(PermissionSet permissionSet, IEnumerable<DriveGrant> driveGrants, SensitiveByteArray driveDecryptionKey)
    {
        _permissionSet = permissionSet;
        _driveGrants = driveGrants;
        _driveDecryptionKey = driveDecryptionKey;
    }

    public bool HasDrivePermission(Guid driveId, DrivePermission permission)
    {
        var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);
        return grant != null && grant.Permission.HasFlag(permission);
    }

    public bool HasPermission(string permission)
    {
        return this._permissionSet.HasKey(permission);
    }

    /// <summary>
    /// Returns the encryption key specific to this app.  This is only available
    /// when the owner is making an HttpRequest.
    /// </summary>
    /// <returns></returns>
    public Guid? GetDriveId(TargetDrive drive)
    {
        var grant = _driveGrants?.SingleOrDefault(g => g.Drive.Alias == drive.Alias && g.Drive.Type == drive.Type);
        return grant?.DriveId;
    }

    /// <summary>
    /// Returns the encryption key specific to this app.  This is only available
    /// when the owner is making an HttpRequest.
    /// </summary>
    /// <returns></returns>
    public SensitiveByteArray GetDriveStorageKey(Guid driveId)
    {
        var grant = _driveGrants?.SingleOrDefault(g => g.DriveId == driveId);

        if (null == grant)
        {
            return null;
        }

        //If we cannot decrypt the storage key BUT the caller has access to the drive,
        //this most likely denotes an anonymous drive.  Return an empty key which means encryption will fail
        if (this._driveDecryptionKey == null || grant.KeyStoreKeyEncryptedStorageKey == null)
        {
            return Array.Empty<byte>().ToSensitiveByteArray();
        }

        var key = this._driveDecryptionKey;
        var storageKey = grant.KeyStoreKeyEncryptedStorageKey.DecryptKeyClone(ref key);
        return storageKey;
    }
}