namespace DotYou.Types
{
    /// <summary>
    /// Specifies the nature of the relationship of the contact to this <see cref="DotYouIdentity"/>.
    /// </summary>
    public enum SystemCircle
    {
        /// <summary>
        /// Any type of contact whether or not they have a <see cref="DotYouIdentity"/>.
        /// </summary>
        PublicAnonymous = 0,

        /// <summary>
        /// The contact has a valid IdentityCertificate on the YouFoundation Network.
        /// </summary>
        Identified = 1,

        /// <summary>
        /// The contact has agreed to be connected with the <see cref="DotYouIdentity"/>.
        /// </summary>
        Connected = 2
    }
}
