namespace Odin.Services.Peer.Encryption
{
    public enum EncryptionType
    {
        Aes = 11,

        /// <summary>
        /// A deposit's key header: sealed to a drive's write-only public key by whoever wrote the file
        /// (<see cref="EncryptedKeyHeader.Sealed"/>).  Nobody on the server can open it.
        /// </summary>
        Sealed = 12,

        /// <summary>
        /// A zero-knowledge drive's own key header: the file key locked with the drive's key by an owner
        /// device, in the same format as <see cref="Aes"/> (<see cref="EncryptedKeyHeader.IsZkDrive"/>).
        /// The server holds no key that opens it, so it stores and returns it as it is
        /// (docs/zero-knowledge-drive-api-plan.md).
        /// </summary>
        ZkDrive = 13
    }
}
