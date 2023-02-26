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
    public readonly struct OdinId
    {
        private readonly string _identifier;
        private readonly Guid _id;

        public OdinId(string identifier)
        {
            _identifier = identifier?.ToLower().Trim();
            if (string.IsNullOrEmpty(_identifier) == false)
            {
                // TODO: Activate this code, but check with Stef & Bishwa what happens when they use international URLs
                // Validate(identifier);  // Important. Validates domain is valid RFC. No funky chars.
                _id = new Guid(HashUtil.ReduceSHA256Hash(_identifier.ToUtf8ByteArray())); // Hm, the chars are guaranteed to be ASCII < 128
            }
            else
            {
                _id = Guid.Empty;
            }
        }

        [JsonIgnore] public string Id => _identifier;

        public bool HasValue()
        {
            return this._id != Guid.Empty;
        }
        public static bool operator ==(OdinId d1, OdinId d2)
        {
            return d1.ToGuidIdentifier() == d2.ToGuidIdentifier();
        }

        public static bool operator !=(OdinId d1, OdinId d2) => !(d1 == d2);

        public static implicit operator string(OdinId dy)
        {
            return dy._identifier;
        }

        public static explicit operator OdinId(string id)
        {
            return new OdinId(id);
        }

        public static implicit operator Guid(OdinId dotYouId)
        {
            return dotYouId.ToGuidIdentifier();
        }

        public override bool Equals(object obj)
        {
            var d2 = (OdinId)obj;
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
            return this._id;
        }

        public byte[] ToByteArray()
        {
            var key = _identifier.ToLower().Trim().ToUtf8ByteArray();
            return key;
        }

        public static OdinId FromByteArray(byte[] id)
        {
            return new OdinId(id.ToStringFromUtf8Bytes());
        }

        public static void Validate(string dotYouId)
        {
            DomainNameValidator.ValidateDomain(dotYouId);
        }
    }
}