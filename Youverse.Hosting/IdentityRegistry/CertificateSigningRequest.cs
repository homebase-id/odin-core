using MessagePack;

namespace Youverse.Hosting.IdentityRegistry
{
    [MessagePackObject]
    public class CertificateSigningRequest
    {
        /// <summary>
        /// Gets or sets the two-letter ISO abbreviation for your country.
        /// </summary>
        [Key(0)]
        public string CountryName { get; set; }

        /// <summary>
        /// Gets or sets the state or province where your organization is located. Can not be abbreviated.
        /// </summary>
        [Key(1)]
        public string State { get; set; }

        /// <summary>
        /// Gets or sets the city where your organization is located.
        /// </summary>
        [Key(2)]
        public string Locality { get; set; }

        /// <summary>
        /// Gets or sets the exact legal name of your organization. Do not abbreviate.
        /// </summary>
        [Key(3)]
        public string Organization { get; set; }

        /// <summary>
        /// Gets or sets the optional organizational information.
        /// </summary>
        [Key(4)]
        public string OrganizationUnit { get; set; }

        /// <summary>
        /// Gets or sets the common name for the CSR.
        /// </summary>
        [Key(5)]
        public string CommonName { get; set; }
    }
}