namespace Youverse.Hosting.Authentication.Perimeter
{
    /// <summary />
    public static class PerimeterAuthConstants
    {
        /// <summary>
        /// Scheme for authenticating external Digital Identity hosts when using Transit. 
        /// </summary>
        public const string TransitCertificateAuthScheme = "TransitCertificate";
        
        /// <summary>
        /// Requests from identities for Connection Requests, Follower subscriptions, etc.  Anything that requires anonymous access to establish
        /// </summary>
        public const string PublicTransitAuthScheme = "PublicTransitCertificate";
        
        /// <summary>
        /// Scheme for allowing identities I follow to send me data
        /// </summary>
        public const string FeedAuthScheme = "FeedAuthScheme";
    }
}