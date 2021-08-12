using System;
using System.Text.Json.Serialization;

namespace DotYou.Types
{
    /// <summary>
    /// Holds the identity for an individual using the dotYou platform
    /// </summary>
    [JsonConverter(typeof(DotYouIdentityConverter))]
    public readonly struct DotYouIdentity
    {
        private readonly string _identifier;

        public DotYouIdentity(string identifier)
        {
            _identifier = identifier?.ToLower();
        }

        [JsonIgnore]
        public string Id => _identifier;

        public static bool operator ==(DotYouIdentity d1, DotYouIdentity d2)
        {
            return string.Equals(d1.ToString(), d2.ToString(), StringComparison.InvariantCultureIgnoreCase);
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
    }
}
