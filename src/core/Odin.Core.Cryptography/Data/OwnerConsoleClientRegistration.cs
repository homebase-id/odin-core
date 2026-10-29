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

        public const int CatType = 100;

        public static readonly Guid Category = Guid.Parse("cc0b390d-ac32-450f-bbaa-0108debde248");

        public int Type => CatType;

        /// <summary>
        /// How long an owner session lives without use. Named so a token meant to behave like the
        /// owner's own login (a YouAuth domain the owner gave no end date to) can share it.
        /// </summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromDays(180);

        public int TimeToLiveSeconds => (Int32)Lifetime.TotalSeconds;

        public Guid CategoryId => Category;

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

        /// <summary>
        /// The session's half of the browser's stored zero-knowledge master key
        /// (docs/zero-knowledge-recovery-plan.md, decision 8).  The browser keeps the other half, the key
        /// locked by this, in its storage; this goes only to a request carrying the session's cookie.
        /// Gone with the session.
        /// </summary>
        public byte[] ZkSessionKey { get; set; }

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