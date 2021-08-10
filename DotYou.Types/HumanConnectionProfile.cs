using System;
using System.Text.Json.Serialization;
using DotYou.Types.DataAttribute;

namespace DotYou.Types
{
    public class HumanConnectionProfile
    {
        private string _givenName;
        private string _surname;
        private DotYouIdentity _dotYouId;

        public HumanConnectionProfile()
        {
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

        /// <summary>
        /// Specifies the DI address for this Human
        /// </summary>
        public DotYouIdentity DotYouId
        {
            get => _dotYouId;
            set
            {
                _dotYouId = value;
                this.Id = MiscUtils.MD5HashToGuid(_dotYouId);
            }
        }

        /// <summary>
        /// A base64 string of this contacts public key certificate.  This must be 
        /// populated if <see cref="SystemCircle"/> is Connected.
        /// </summary>
        public string PublicKeyCertificate { get; set; }

        private NameAttribute Name
        {
            get;
            set;
        }

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
