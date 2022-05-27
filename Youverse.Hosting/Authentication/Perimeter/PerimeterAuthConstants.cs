namespace Youverse.Hosting.Authentication.Perimeter
{
    public static class PerimeterAuthConstants
    {
        /// <summary>
        /// Scheme for authenticating external Digital Identity hosts when using Transit. 
        /// </summary>
        public const string TransitCertificateAuthScheme = "TransitCertificate";
        
        /// <summary>
        /// Scheme for authenticating external Digital Identity hosts when sending notifications. 
        /// </summary>
        public const string NotificationCertificateAuthScheme = "NotificationCertificate";

        public const string PublicTransitAuthScheme = "PublicTransitCertificate";
    }
}