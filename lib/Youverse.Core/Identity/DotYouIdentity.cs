using System;
using System.Linq;
using System.Text.Json.Serialization;
using Youverse.Core.Util;

namespace Youverse.Core.Identity
{
    /// <summary>
    /// Holds the identity for an individual using the dotYou platform
    /// </summary>
    [JsonConverter(typeof(DotYouIdentityConverter))]
    public readonly struct DotYouIdentity
    {
        private readonly string _identifier;
        private readonly Guid _hash;

        /// <summary>
        /// Guaranteed to hold a trimmed, RFC compliant domain name and unique HASH of the name
        /// </summary>
        /// <param name="identifier">The domain name (ASCII 127) as per the RFC</param>
        /// <exception cref="ArgumentNullException"></exception>
        public DotYouIdentity(string identifier)
        {
            if (identifier == null) 
                throw new ArgumentNullException(nameof(identifier));

            var s = identifier?.ToLower().Trim();
            DomainNameValidator.TryValidateDomain(s);

            _identifier = s;

            // I would have preferred if the HASH was evaluated lazily. But that's not possible with a RO struct.
            _hash = new Guid(HashUtil.ReduceSHA256Hash(_identifier.ToUtf8ByteArray()));
        }

        [JsonIgnore] public string Id => _identifier;

        public bool HasValue()
        {
            return this._hash != Guid.Empty;
        }
        public static bool operator ==(DotYouIdentity d1, DotYouIdentity d2)
        {
            return d1.ToGuidIdentifier() == d2.ToGuidIdentifier();
        }

        public static bool operator !=(DotYouIdentity d1, DotYouIdentity d2) => !(d1 == d2);

        public static implicit operator string(DotYouIdentity dy)
        {
            return dy._identifier;
        }

        public static explicit operator DotYouIdentity(string id)
        {
            return new DotYouIdentity(id);
        }

        public static implicit operator Guid(DotYouIdentity dotYouId)
        {
            return dotYouId.ToGuidIdentifier();
        }

        public override bool Equals(object obj)
        {
            var d2 = (DotYouIdentity)obj;
            return this == d2;
        }

        public override int GetHashCode()
        {
            return _identifier?.GetHashCode() ?? 0;
        }

        public override string ToString()
        {
            return _identifier;
        }

        /// <summary>
        /// Returns an UniqueId of this DotYouIdentity using a hash to converted to a Guid.  The value is converted to lower case before calculating the hash.
        /// </summary>
        /// <returns></returns>
        public Guid ToGuidIdentifier()
        {
            return this._hash;
        }

        public byte[] ToByteArray()
        {
            var key = _identifier.ToUtf8ByteArray();
            return key;
        }

        public static DotYouIdentity FromByteArray(byte[] id)
        {
            return new DotYouIdentity(id.ToStringFromUtf8Bytes());
        }

        public static void Validate(string dotYouId)
        {
            DomainNameValidator.TryValidateDomain(dotYouId);
        }
    }
}