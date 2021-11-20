namespace Youverse.Core.Services.Transit.Encryption
{
    /// <summary>
    /// The encrypted version of the KeyHeader for a given recipient
    /// which as been encrypted using the RecipientTransitPublicKey
    /// </summary>
    public class EncryptedRecipientTransferKeyHeader
    {
        public int EncryptionVersion { get; set; }
        public byte[] Data { get; set; }
    }
}