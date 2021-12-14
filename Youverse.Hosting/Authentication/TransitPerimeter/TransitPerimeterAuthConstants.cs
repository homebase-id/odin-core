namespace Youverse.Hosting.Authentication.TransitPerimeter
{
    public static class TransitPerimeterAuthConstants
    {
        /// <summary>
        /// Scheme for authenticating external Digital Identity hosts. 
        /// </summary>
        //TODO: determine why I cannot use my own name here.  I must use 'certificate'
        public const string TransitAuthScheme = "Certificate";
    }
}