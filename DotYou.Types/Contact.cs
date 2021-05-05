using System;
namespace DotYou.Types
{

    public class Contact
    {
        public DotYouIdentity? DotYouId { get; set; }

        public string GivenName { get; set; }
        public string Surname { get; set; }

        public string Tag { get; set; }

        public string PrimaryEmail { get; set; }

        /// <summary>
        /// A hexidecimal string of this contacts public key certificate.  This must be 
        /// populated if <see cref="SystemCircle"/> is <see cref="SystemCircle.Connected"/>.
        /// </summary>
        public string PublicKeyCertificate { get; set; }

        /// <summary>
        /// Specifies which system circle this contact exists with-in.
        /// </summary>
        public SystemCircle SystemCircle { get; set; }

        public override string ToString()
        {
            return $"{GivenName} {Surname}";
        }
    }
}
