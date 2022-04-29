using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    /// <summary>
    /// A set of drive grants and keys to exchange data with this identity.  This can be used for connections
    /// between two identities or integration with a 3rd party
    /// </summary>
    public class ExchangeGrant
    {
        public Guid Id { get; set; }

        public UInt64 Created { get; set; }
        
        public UInt64 Modified { get; set; }

        public SymmetricKeyEncryptedAes MasterKeyEncryptedKeyStoreKey { get; set; }

        public bool IsRevoked { get; set; }

        public List<DriveGrant> KeyStoreKeyEncryptedDriveGrants { get; set; }

        public void AssertValidRemoteKey(SensitiveByteArray halfKey)
        {
            throw new NotImplementedException("what to do here?");
        }
    }
}