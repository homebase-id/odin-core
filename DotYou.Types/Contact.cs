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

        public string CertificateThumprint { get; set; }

        /*
        public NetworkCircle NetworkConnectionType
        {
            get
            {
                return this.RelationshipId == Guid.Empty ? NetworkCircle.None : NetworkCircle.Connected;
            }
        }
        */
        public override string ToString()
        {
            return $"{GivenName} {Surname}";
        }
    }

    /// <summary>
    /// Specifies the nature of the relationship of the contact to this <see cref="DotYouIdentity"/>.
    /// </summary>
    public enum NetworkCircle
    {
        /// <summary>
        /// Any type of contact whether or not they have a <see cref="DotYouIdentity"/>
        /// </summary>
        PublicAnonymous = 0,

        /// <summary>
        /// The contact has a valid certificate
        /// </summary>
        Identified = 1,

        /// <summary>
        /// 
        /// </summary>
        Connected = 2
    }
}
