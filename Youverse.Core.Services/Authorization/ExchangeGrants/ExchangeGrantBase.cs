using System;
using System.Collections.Generic;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Permissions;

namespace Youverse.Core.Services.Authorization.ExchangeGrants
{
    /// <summary>
    /// A set of drive grants and keys to exchange data with this identity.  This can be used for connections
    /// between two identities or integration with a 3rd party
    /// </summary>
    public abstract class ExchangeGrantBase
    {
        public Guid Id { get; set; }

        public UInt64 Created { get; set; }


        public UInt64 Modified { get; set; }

        public SymmetricKeyEncryptedAes MasterKeyEncryptedKeyStoreKey { get; set; }

        public bool IsRevoked { get; set; }

        public List<DriveGrant> KeyStoreKeyEncryptedDriveGrants { get; set; }

        /// <summary>
        /// Permissions indicating what the app can do
        /// </summary>
        public PermissionSet PermissionSet { get; set; }

        //TODO: add app grants
    }

    /// <summary>
    /// Specifies an exchange grant given to a DotYouId
    /// </summary>
    public class IdentityExchangeGrant : ExchangeGrantBase
    {
        /// <summary>
        /// The DotYouId being granted access
        /// </summary>
        public DotYouIdentity DotYouId { get; set; }

        internal static IdentityExchangeGrant FromLiteDbRecord(ExchangeGrantLiteDbRecord record)
        {
            return new IdentityExchangeGrant()
            {
                DotYouId = record.DotYouId,
                Id = record.Id,
                Created = record.Created,
                Modified = record.Modified,
                IsRevoked = record.IsRevoked,
                PermissionSet = record.PermissionSet,
                KeyStoreKeyEncryptedDriveGrants = record.KeyStoreKeyEncryptedDriveGrants,
                MasterKeyEncryptedKeyStoreKey = record.MasterKeyEncryptedKeyStoreKey
            };
        }
        internal ExchangeGrantLiteDbRecord ToLiteDbRecord()
        {
            return new ExchangeGrantLiteDbRecord()
            {
                StorageType = ExchangeGranteeType.Identity,
                DotYouId = this.DotYouId,
                Id = this.Id,
                Created = this.Created,
                Modified = this.Modified,
                IsRevoked = this.IsRevoked,
                PermissionSet = this.PermissionSet,
                KeyStoreKeyEncryptedDriveGrants = this.KeyStoreKeyEncryptedDriveGrants,
                MasterKeyEncryptedKeyStoreKey = this.MasterKeyEncryptedKeyStoreKey
            };
        }
    }

    public class AppExchangeGrant : ExchangeGrantBase
    {
        /// <summary>
        /// The app being granted access
        /// </summary>
        public Guid AppId { get; set; }
        
        internal ExchangeGrantLiteDbRecord ToLiteDbRecord()
        {
            return new ExchangeGrantLiteDbRecord()
            {
                StorageType = ExchangeGranteeType.Identity,
                AppId = this.AppId,
                Id = this.Id,
                Created = this.Created,
                Modified = this.Modified,
                IsRevoked = this.IsRevoked,
                PermissionSet = this.PermissionSet,
                KeyStoreKeyEncryptedDriveGrants = this.KeyStoreKeyEncryptedDriveGrants,
                MasterKeyEncryptedKeyStoreKey = this.MasterKeyEncryptedKeyStoreKey
            };
        }
        
        internal static AppExchangeGrant FromLiteDbRecord(ExchangeGrantLiteDbRecord record)
        {
            return new AppExchangeGrant()
            {
                AppId = record.AppId,
                Id = record.Id,
                Created = record.Created,
                Modified = record.Modified,
                IsRevoked = record.IsRevoked,
                PermissionSet = record.PermissionSet,
                KeyStoreKeyEncryptedDriveGrants = record.KeyStoreKeyEncryptedDriveGrants,
                MasterKeyEncryptedKeyStoreKey = record.MasterKeyEncryptedKeyStoreKey
            };
        }
    }

    /// <summary>
    /// Adapter class for storing in litedb.
    /// </summary>
    public class ExchangeGrantLiteDbRecord : ExchangeGrantBase
    {
        public ExchangeGranteeType StorageType { get; set; }

        public Guid AppId { get; set; }

        public DotYouIdentity DotYouId { get; set; }
    }

    public enum ExchangeGranteeType
    {
        App = 1,
        Identity = 2
    }
}