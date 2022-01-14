using System;

namespace Youverse.Core.Services.Transit.Encryption
{
    /// <summary>
    /// The encrypted version of the KeyHeader for a given recipient
    /// which as been encrypted using the RecipientTransitPublicKey
    /// </summary>
    public class RsaEncryptedRecipientTransferKeyHeader
    {
        public UInt32 PublicKeyCrc { get; set; }

        public byte[] EncryptedAesKey { get; set; }
    }
}