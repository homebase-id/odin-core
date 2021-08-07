using System;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace DotYou.Types
{
    public class Person
    {
        private string _givenName;
        private string _surname;

        public Person()
        {
            Id = Guid.NewGuid();
        }
        
        public Guid Id { get; set; }
        
        /// <summary>
        /// A unique id based on md5hash(lcase(<see cref="GivenName"/> + <see cref="Surname"/>)) to be used for 
        /// </summary>
        public Guid NameUniqueId
        {
            get;
            set;
        }

        public DotYouIdentity? DotYouId { get; set; }

        public string GivenName
        {
            get => _givenName;
            set
            {
                _givenName = value;
                this.UpdateNameHash();

            }
        }

        public string Surname
        {
            get => _surname;
            set
            {
                _surname = value;
                this.UpdateNameHash();
            }
        }

        private void UpdateNameHash()
        {
            this.NameUniqueId = MiscUtils.MD5HashToGuid(_givenName + _surname);
        }

        public string Tag { get; set; }

        public string PrimaryEmail { get; set; }

        /// <summary>
        /// A base64 string of this contacts public key certificate.  This must be 
        /// populated if <see cref="SystemCircle"/> is <see cref="SystemCircle.Connected"/>.
        /// </summary>
        public string PublicKeyCertificate { get; set; }

        /// <summary>
        /// Specifies which system circle this contact exists with-in.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SystemCircle SystemCircle { get; set; }

        public override string ToString()
        {
            return $"{GivenName} {Surname}";
        }
    }
}
