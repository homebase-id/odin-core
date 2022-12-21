using System.Collections.Generic;
using System.Linq;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.Permissions;

namespace Youverse.Core.Services.Authorization.ExchangeGrants;

/// <summary>
/// Defines the information needed to grant system permissions and drive access
/// </summary>
public class ExchangeGrant
{
    public ulong Created { get; set; }
    public ulong Modified { get; set; }
    public SymmetricKeyEncryptedAes MasterKeyEncryptedKeyStoreKey { get; set; }
    public bool IsRevoked { get; set; }
    public List<DriveGrant> KeyStoreKeyEncryptedDriveGrants { get; set; }
    public PermissionSet PermissionSet { get; set; }

    public RedactedExchangeGrant Redacted()
    {
        return new RedactedExchangeGrant()
        {
            IsRevoked = this.IsRevoked,
            PermissionSet = this.PermissionSet,
            DriveGrants = this.KeyStoreKeyEncryptedDriveGrants.Select(cg => cg.Redacted()).ToList()
        };
    }
}

public class RedactedExchangeGrant
{
    public bool IsRevoked { get; set; }
    public PermissionSet PermissionSet { get; set; }
    public List<RedactedDriveGrant> DriveGrants { get; set; }
}