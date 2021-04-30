using System;
using System.Text.Json.Serialization;

namespace DotYou.Types
{
    /// <summary>
    /// Holds the identity for an individual using the dotYou platform
    /// </summary>
    [JsonConverter(typeof(DotYouIdentityConverter))]
    public struct DotYouIdentity
    {
        private string _identifier;

        public DotYouIdentity(string identifier)
        {
            if (string.IsNullOrEmpty(identifier) || string.IsNullOrWhiteSpace(identifier))
            {
                throw new ArgumentNullException(nameof(identifier));
            }

            //TODO: add other validations such as format (

            _identifier = identifier;
        }

        [JsonIgnore]
        public string Id
        {
            get
            {
                return _identifier;
            }
        }

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
            return _identifier.GetHashCode();
        }
        public override string ToString()
        {
            return _identifier;
        }
    }
}
