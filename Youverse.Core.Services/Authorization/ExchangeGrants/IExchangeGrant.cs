using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.Permissions;

namespace Youverse.Core.Services.Authorization.ExchangeGrants;

/// <summary>
/// Defines the information needed to grant system permissions and drive access
/// </summary>
public interface IExchangeGrant
{
    public UInt64 Created { get; set; }

    public UInt64 Modified { get; set; }

    public SymmetricKeyEncryptedAes MasterKeyEncryptedKeyStoreKey { get; set; }

    public bool IsRevoked { get; set; }

    public List<DriveGrant> KeyStoreKeyEncryptedDriveGrants { get; set; }

    /// <summary>
    /// Permissions indicating what the app can do
    /// </summary>
    public PermissionSet PermissionSet { get; set; }

    public PermissionFlags PermissionFlags { get; set; }
}