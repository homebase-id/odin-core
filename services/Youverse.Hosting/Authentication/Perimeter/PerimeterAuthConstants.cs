namespace Youverse.Hosting.Authentication.Perimeter
{
    public static class PerimeterAuthConstants
    {
        /// <summary>
        /// Scheme for authenticating external Digital Identity hosts when using Transit. 
        /// </summary>
        public const string TransitCertificateAuthScheme = "TransitCertificate";
        
        public const string PublicTransitAuthScheme = "PublicTransitCertificate";
    }
}