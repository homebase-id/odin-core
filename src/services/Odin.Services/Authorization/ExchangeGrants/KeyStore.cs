using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Odin.Core.Cryptography.Data;
using Odin.Core.Time;
using Odin.Services.Authorization.Permissions;

namespace Odin.Services.Authorization.ExchangeGrants;

/// <summary>
/// Defines the information needed to grant system permissions and drive access
/// </summary>
public class KeyStore
{
    public UnixTimeUtc Created { get; set; }
    public UnixTimeUtc Modified { get; set; }
    
    public SymmetricKeyEncryptedAes MasterKeyEncryptedKeyStoreKey { get; set; }
    
    public bool IsRevoked { get; set; }
    
    [JsonPropertyName("keyStoreKeyEncryptedDriveGrants")]
    public List<DriveGrant> DriveGrants { get; set; }

    public PermissionSet PermissionSet { get; set; }

    public SymmetricKeyEncryptedAes KeyStoreKeyEncryptedIcrKey { get; set; }

    public RedactedExchangeGrant Redacted()
    {
        return new RedactedExchangeGrant()
        {
            IsRevoked = this.IsRevoked,
            PermissionSet = this.PermissionSet,
            HasIcrKey = this.KeyStoreKeyEncryptedIcrKey?.KeyEncrypted?.Length > 0,
            DriveGrants = this.DriveGrants.Select(cg => cg.Redacted()).ToList()
        };
    }
}

public class RedactedExchangeGrant
{
    public bool IsRevoked { get; set; }
    public PermissionSet PermissionSet { get; set; }
    public List<RedactedDriveGrant> DriveGrants { get; set; }

    public bool HasIcrKey { get; set; }
}