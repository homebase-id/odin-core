using System;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Services.Security.ZeroKnowledge;

namespace Odin.Services.Peer.Encryption
{
    public class EncryptedKeyHeader
    {
        public int EncryptionVersion { get; set; }

        public EncryptionType Type { get; set; }


        public byte[] Iv { get; set; }

        /// <summary>
        /// The encrypted bytes of the Aes key from the original key header
        /// </summary>
        public byte[] EncryptedAesKey { get; set; }

        /// <summary>
        /// Decrypts this Encrypted Key header
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        /// <exception cref="OdinSecurityException"></exception>
        /// <remarks>
        /// Refuses a sealed key header (a deposit) and a zero-knowledge drive's own: nobody here can open
        /// either.
        /// </remarks>
        public KeyHeader DecryptAesToKeyHeader(ref SensitiveByteArray key)
        {
            if (IsLockedByClient())
            {
                throw new OdinClientException($"A {Type} key header can't be opened on the server",
                    OdinClientErrorCode.InvalidKeyHeader);
            }

            if (this.EncryptionVersion == 1)
            {
                var bytes = AesCbc.Decrypt(this.EncryptedAesKey, key, this.Iv);
                var kh = KeyHeader.FromCombinedBytes(bytes, 16, 16);
                bytes.ToSensitiveByteArray().Wipe();
                return kh;
            }

            throw new OdinSecurityException("Unsupported encryption version");
        }

        /// <summary>
        /// For the paths that take a deposit as it comes (a peer transfer, the inbox): a sealed key header
        /// passes through as a <see cref="ClientLockedKeyHeader"/>, which the write stores as it is; any
        /// other is decrypted.
        /// </summary>
        public KeyHeader DecryptOrPassSealed(ref SensitiveByteArray key) =>
            IsPassedOnAsSent() ? new ClientLockedKeyHeader(this) : DecryptAesToKeyHeader(ref key);

        public static EncryptedKeyHeader EncryptKeyHeaderAes(KeyHeader keyHeader, byte[] iv, ref SensitiveByteArray key)
        {
            if (keyHeader is ClientLockedKeyHeader locked)
            {
                // A deposit goes on sealed, as it was written, and a member's "keep the file key" and a comment's file key
                // as they were sent.  A zero-knowledge drive's own has no AES key here: re-locking it would make a key
                // header that opens to nothing
                return locked.Locked.IsPassedOnAsSent()
                    ? locked.Locked
                    : throw new OdinClientException("A zero-knowledge drive's key header can't be re-locked on the server",
                        OdinClientErrorCode.InvalidKeyHeader);
            }

            var secureKeyHeader = keyHeader.Combine();
            var data = AesCbc.Encrypt(secureKeyHeader.GetKey(), key, iv);
            secureKeyHeader.Wipe();

            return new EncryptedKeyHeader()
            {
                EncryptionVersion = 1,
                Type = EncryptionType.Aes,
                Iv = iv,
                EncryptedAesKey = data
            };
        }

        /// <summary>
        /// A deposit's key header: the file's key header (IV and AES key, as <see cref="KeyHeader.Combine"/>
        /// joins them) sealed to a drive's write-only public key.  The IV is also kept as is, as for an
        /// AES key header.
        /// </summary>
        public static EncryptedKeyHeader SealedTo(KeyHeader fileKey, string publicKeyJwk) =>
            Sealed(fileKey.Iv, ZkSeal.Seal(fileKey.Combine().GetKey(), publicKeyJwk));

        public static EncryptedKeyHeader Sealed(byte[] iv, byte[] sealedKeyHeader) => new()
        {
            EncryptionVersion = 1,
            Type = EncryptionType.Sealed,
            Iv = iv,
            EncryptedAesKey = sealedKeyHeader
        };

        public bool IsSealed() => Type == EncryptionType.Sealed;

        /// <summary>
        /// A zero-knowledge drive's own key header: the file's key header locked with the drive's key by
        /// an owner device, as <see cref="EncryptKeyHeaderAes"/> locks one (AES-CBC of IV and AES key,
        /// the IV kept as is), only with a key the server doesn't hold.
        /// </summary>
        public bool IsZkDrive() => Type == EncryptionType.ZkDrive;

        /// <summary>A member's key to a file shared with their circle (<see cref="EncryptionType.Share"/>), passed on as it is.</summary>
        public bool IsShare() => Type == EncryptionType.Share;

        /// <summary>
        /// A member's update keeping a shared file's key (decision C of docs/selective-share-pr3-plan.md): a
        /// <see cref="EncryptionType.Share"/> key header with the new IV and nothing else.
        /// </summary>
        public void AssertKeepsKey()
        {
            if (!IsShare() || !HoldsIvOnly())
            {
                throw new OdinClientException("A member's update keeps the file key: a share key header with a new IV and no key",
                    OdinClientErrorCode.InvalidKeyHeader);
            }
        }

        /// <summary>A comment encrypted with its file's key (<see cref="EncryptionType.FileKey"/>): its own IV only.</summary>
        public bool IsFileKey() => Type == EncryptionType.FileKey;

        /// <summary>Refuses a file key header that isn't one: version 1, a strong IV, and no key (<see cref="IsFileKey"/>).</summary>
        public void AssertValidFileKey()
        {
            if (!IsFileKey() || EncryptionVersion != 1 || !HoldsIvOnly())
            {
                throw new OdinClientException("A comment's file key header holds its own IV and no key", OdinClientErrorCode.InvalidKeyHeader);
            }
        }

        /// <summary>A member's key to a shared file: the share's locked file key, with the file's own IV.</summary>
        public static EncryptedKeyHeader ForShare(byte[] iv, byte[] lockedFileKey) => new()
        {
            EncryptionVersion = 1,
            Type = EncryptionType.Share,
            Iv = iv,
            EncryptedAesKey = lockedFileKey
        };

        /// <summary>
        /// A key header a client locked with a key the server doesn't hold: a deposit, a zero-knowledge drive's own, or a
        /// comment's on its file's key.
        /// </summary>
        public bool IsLockedByClient() => IsSealed() || IsZkDrive() || IsFileKey();

        /// <summary>
        /// Locked by a client (<see cref="IsLockedByClient"/>), or a member's share key: nothing the server can open,
        /// so it passes it on as it is.
        /// </summary>
        public bool IsOpaqueToServer() => IsLockedByClient() || IsShare();

        /// <summary>
        /// What a write or a transfer passes on as it came, never opened or re-locked: a deposit, a member's share key
        /// header keeping a file's key, a comment's on its file's key.  A zero-knowledge drive's own is not: it is stored
        /// as sent but never travels.
        /// </summary>
        public bool IsPassedOnAsSent() => IsSealed() || IsShare() || IsFileKey();

        // A strong IV and no key: what keeping a file's key or using its file's key sends
        private bool HoldsIvOnly() => ByteArrayUtil.IsStrongKey(Iv) && !(EncryptedAesKey?.Length > 0);

        /// <summary>
        /// Whether this has the shape of a zero-knowledge drive's key header: version 1, a strong IV, and the
        /// 48 bytes that AES-CBC makes of a 16-byte IV and a 16-byte key.  Nothing more can be checked:
        /// nobody here can open it.  The type isn't checked: a drive converted keeping its key relabels its
        /// <c>aes</c> headers that have this shape.
        /// </summary>
        public bool HasZkDriveShape() => EncryptionVersion == 1 && ByteArrayUtil.IsStrongKey(Iv) && EncryptedAesKey?.Length == 48;

        /// <summary>Refuses a zero-knowledge drive's key header that isn't one (<see cref="HasZkDriveShape"/>).</summary>
        public void AssertValidZkDrive()
        {
            if (!HasZkDriveShape())
            {
                throw new OdinClientException("The zero-knowledge drive key header is malformed", OdinClientErrorCode.InvalidKeyHeader);
            }
        }

        /// <summary>
        /// Refuses a sealed key header that isn't one: the IV must be strong and the key header a
        /// sealed value.  Nothing more can be checked: nobody here can open it.
        /// </summary>
        public void AssertValidSealed()
        {
            if (EncryptionVersion != 1 || !ByteArrayUtil.IsStrongKey(Iv) || !ZkSeal.IsSealed(EncryptedAesKey))
            {
                throw new OdinClientException("The sealed key header is malformed", OdinClientErrorCode.InvalidKeyHeader);
            }
        }

        public SensitiveByteArray Combine()
        {
            //TODO: I Dont know the length of encrypted AES Key so maybe base64 encode this instead?
            return new SensitiveByteArray(ByteArrayUtil.Combine(this.Iv, this.EncryptedAesKey));
        }

        public static EncryptedKeyHeader Empty()
        {
            var empty = Guid.Empty.ToByteArray();
            var emptySba = empty.ToSensitiveByteArray();
            return new EncryptedKeyHeader()
            {
                EncryptionVersion = 1,
                Type = EncryptionType.Aes,
                Iv = empty,
                EncryptedAesKey = ByteArrayUtil.Combine(empty, empty, empty)
            };
            // return EncryptKeyHeaderAes(KeyHeader.Empty(), empty, ref emptySba);
        }

        public string ToBase64()
        {
            byte[] versionBytes = BitConverter.GetBytes(this.EncryptionVersion);
            var combinedBytes = ByteArrayUtil.Combine(this.Iv, this.EncryptedAesKey, versionBytes);
            var encryptedKeyHeader64 = Convert.ToBase64String(combinedBytes);
            combinedBytes.ToSensitiveByteArray().Wipe();
            return encryptedKeyHeader64;
        }

        public static EncryptedKeyHeader FromBase64(string data64)
        {
            var bytes = Convert.FromBase64String(data64);
            var (iv, encryptedAesKey, version) = ByteArrayUtil.Split(bytes, 16, 48, 4);
            return new EncryptedKeyHeader()
            {
                Iv = iv,
                EncryptedAesKey = encryptedAesKey,
                EncryptionVersion = BitConverter.ToInt32(version)
            };
        }
    }
}