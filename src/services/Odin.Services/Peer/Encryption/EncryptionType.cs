namespace Odin.Services.Peer.Encryption
{
    public enum EncryptionType
    {
        Aes = 11,

        /// <summary>
        /// A deposit's key header: sealed to a drive's write-only public key by whoever wrote the file
        /// (<see cref="EncryptedKeyHeader.Sealed"/>).  Nobody on the server can open it.
        /// </summary>
        Sealed = 12
    }
}
