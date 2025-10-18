using System;
using Odin.Core.Serialization;

namespace Odin.Core.Cryptography.Data
{
    public class OwnerConsoleClientRegistration : IClientRegistration, IDisposable
    {
        private Guid _id;

        ~OwnerConsoleClientRegistration()
        {
            this.Dispose();
        }

        public Guid Id
        {
            get => _id;
            set => _id = value;
        }

        public string IssuedTo { get; set; }

        public int Type { get; set; }

        public long TimeToLiveSeconds { get; set; }
        
        public Guid CategoryId { get; set; }

        public string GetValue()
        {
            return OdinSystemSerializer.Serialize(this);
        }

        /// <summary>
        /// Point in time the token expires
        /// </summary>
        public Int64 ExpiryUnixTime { get; set; }

        /// <summary>
        /// The Server's 1/2 of the KeK
        /// </summary>
        // public byte[] HalfKey { get; set; }
        public SymmetricKeyEncryptedXor TokenEncryptedKek { get; set; }

        /// <summary>
        /// The shared secret between the client and the host
        /// </summary>
        public byte[] SharedSecret { get; set; }

        public NonceTable NonceKeeper { get; set; }

        public void Dispose()
        {
            // TODO: How to delete ServerHalfOwnerConsoleKey ?
            // ByteArrayUtil.WipeByteArray(this.HalfKey);
            //ByteArrayUtil.WipeByteArray(this.SharedSecret);
        }
    }
}