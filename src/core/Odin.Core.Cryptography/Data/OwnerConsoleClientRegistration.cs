using System;
using Odin.Core.Serialization;

namespace Odin.Core.Cryptography.Data
{
    public class OwnerConsoleClientRegistration : IClientRegistration, IDisposable
    {
        public OwnerConsoleClientRegistration()
        {
            // for json
        }

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

        public string IssuedTo { get; init; }

        public int Type => 100;

        public int TimeToLiveSeconds => (Int32)TimeSpan.FromDays(180).TotalSeconds;

        public Guid CategoryId => Guid.Parse("cc0b390d-ac32-450f-bbaa-0108debde248");

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
        public SymmetricKeyEncryptedXor TokenEncryptedKek { get; set; }

        /// <summary>
        /// The shared secret between the client and the host
        /// </summary>
        public byte[] SharedSecret { get; set; }

        public NonceTable NonceKeeper { get; set; }

        public void ExtendTokenLife()
        {
            this.ExpiryUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)TimeSpan.FromDays(30 * 6).TotalSeconds;
        }
        
        public void Dispose()
        {
            // TODO: How to delete ServerHalfOwnerConsoleKey ?
            // ByteArrayUtil.WipeByteArray(this.HalfKey);
            //ByteArrayUtil.WipeByteArray(this.SharedSecret);
        }
    }
}