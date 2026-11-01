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
        ZkDrive = 13,

        /// <summary>
        /// What a member of a Selective Share circle gets for a file shared with it: the file key locked with the
        /// circle's key (the share row's locked file key) and the file's own IV.  Only ever sent to a member, never
        /// taken or stored as a file's key header (docs/selective-share-pr3-plan.md, section 6).
        /// </summary>
        Share = 14
    }
}
