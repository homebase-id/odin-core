using System.Collections.Generic;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.Permissions;

namespace Youverse.Core.Services.Authorization.ExchangeGrants;

internal class ExchangeGrant : IExchangeGrant
{
    public ulong Created { get; set; }
    public ulong Modified { get; set; }
    public SymmetricKeyEncryptedAes MasterKeyEncryptedKeyStoreKey { get; set; }
    public bool IsRevoked { get; set; }
    public List<DriveGrant> KeyStoreKeyEncryptedDriveGrants { get; set; }
    public PermissionSet PermissionSet { get; set; }

    public PermissionFlags PermissionFlags { get; set; }
}