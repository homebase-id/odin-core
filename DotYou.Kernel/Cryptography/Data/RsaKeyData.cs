
using System;

namespace DotYou.Kernel.Services.Admin.Authentication
{
    public class RsaKeyData
    {
        // public byte[] publicKey;
        // public byte[] privateKey;   // Can we allow it to be encrypted?
        // public UInt32 crc32c;       // The CRC32C of the public key
        // public UInt64 expiration;   // Time when this key expires
        // public UInt64 instantiated; // Time when this key was made available
        // public Guid iv;             // If encrypted, this will hold the IV
        // public bool encrypted;      // If false then privateKey is the XML, otherwise it's AES-CBC base64 encrypted

        public byte[] publicKey { get; set; }
        public byte[] privateKey{ get; set; }   // Can we allow it to be encrypted?
        public UInt32 crc32c { get; set; }       // The CRC32C of the public key
        public UInt64 expiration { get; set; }   // Time when this key expires
        public UInt64 instantiated { get; set; } // Time when this key was made available
        public Guid iv { get; set; }             // If encrypted, this will hold the IV
        public bool encrypted { get; set; }      // If false then privateKey is the XML, otherwise it's AES-CBC base64 encrypted
    }
}