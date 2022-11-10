namespace Youverse.Core.Services.Registry
{
    public class CertificateSigningRequest
    {
        /// <summary>
        /// Gets or sets the two-letter ISO abbreviation for your country.
        /// </summary>
        public string CountryName { get; set; }

        /// <summary>
        /// Gets or sets the state or province where your organization is located. Can not be abbreviated.
        /// </summary>
        public string State { get; set; }

        /// <summary>
        /// Gets or sets the city where your organization is located.
        /// </summary>
        public string Locality { get; set; }

        /// <summary>
        /// Gets or sets the exact legal name of your organization. Do not abbreviate.
        /// </summary>
        public string Organization { get; set; }

        /// <summary>
        /// Gets or sets the optional organizational information.
        /// </summary>
        public string OrganizationUnit { get; set; }

        /// <summary>
        /// Gets or sets the common name for the CSR.
        /// </summary>
        public string CommonName { get; set; }
    }
}