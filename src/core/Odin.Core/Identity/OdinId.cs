using System;
using System.Text.Json.Serialization;
using Odin.Core.Util;

namespace Odin.Core.Identity
{
    /// <summary>
    /// Holds the identity for an individual using the dotYou platform
    /// </summary>
    [JsonConverter(typeof(OdinIdConverter))]
    public readonly struct OdinId
    {
        private readonly AsciiDomainName _domainName;
        private readonly Guid _hash;

        /// <summary>
        /// Guaranteed to hold a trimmed, RFC compliant domain name and unique HASH of the name
        /// </summary>
        /// <param name="identifier">The domain name (ASCII 127) as per the RFC</param>
        /// <exception cref="ArgumentNullException"></exception>
        public OdinId(string identifier)
        {
            if (identifier == null)
                throw new ArgumentNullException(nameof(identifier));


            _domainName = new AsciiDomainName(identifier);

            // I would have preferred if the HASH was evaluated lazily. But that's not possible with a RO struct.
            _hash = new Guid(ByteArrayUtil.ReduceSHA256Hash(_domainName.DomainName.ToUtf8ByteArray()));
        }

        public OdinId(AsciiDomainName asciiDomain)
        {
            _domainName = asciiDomain;

            // I would have preferred if the HASH was evaluated lazily. But that's not possible with a RO struct.
            _hash = new Guid(ByteArrayUtil.ReduceSHA256Hash(_domainName.DomainName.ToUtf8ByteArray()));
        }


        [JsonIgnore] public string DomainName => _domainName.DomainName;
        [JsonIgnore] public AsciiDomainName AsciiDomain => _domainName;

        public bool HasValue()
        {
            return this._hash != Guid.Empty;
        }

        public static bool operator ==(OdinId d1, OdinId d2)
        {
            return d1.ToHashId() == d2.ToHashId();
        }

        public static bool operator !=(OdinId d1, OdinId d2) => !(d1 == d2);

        public static implicit operator string(OdinId dy)
        {
            return dy._domainName.DomainName;
        }

        public static explicit operator OdinId(string id)
        {
            return new OdinId(id);
        }

        public static implicit operator AsciiDomainName(OdinId dy)
        {
            return dy._domainName;
        }

        public static explicit operator OdinId(AsciiDomainName id)
        {
            return new OdinId(id);
        }

        public static implicit operator Guid(OdinId odinId)
        {
            return odinId.ToHashId();
        }

        public override bool Equals(object obj)
        {
            var d2 = (OdinId)obj;
            return this == d2;
        }

        public override int GetHashCode()
        {
            return _domainName.DomainName?.GetHashCode() ?? 0;
        }

        public override string ToString()
        {
            return _domainName.DomainName?.ToLower();
        }

        /// <summary>
        /// Returns an UniqueId of this OdinId using a hash to converted to a Guid.  The value is converted to lower case before calculating the hash.
        /// </summary>
        /// <returns></returns>
        public Guid ToHashId()
        {
            return this._hash;
        }

        public static Guid ToHashId(AsciiDomainName domainName)
        {
            return new Guid(ByteArrayUtil.ReduceSHA256Hash(domainName.DomainName.ToUtf8ByteArray()));
        }

        public byte[] ToByteArray()
        {
            var key = _domainName.DomainName.ToUtf8ByteArray();
            return key;
        }

        public static OdinId FromByteArray(byte[] id)
        {
            return new OdinId(id.ToStringFromUtf8Bytes());
        }

        public static bool IsValid(string odinId)
        {
            return AsciiDomainNameValidator.TryValidateDomain(odinId);
        }

        public static void Validate(string odinId)
        {
            AsciiDomainNameValidator.AssertValidDomain(odinId);
        }
    }
}