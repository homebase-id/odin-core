namespace Youverse.Core.Services.Authorization.Acl
{
    //TODO: should we have an OwnerOnly?
    public enum SecurityGroupType
    {
        /// <summary>
        /// Indicates anyone on the internet (i.e. public)
        /// </summary>
        Anonymous = 11,

        /// <summary>
        /// TODO: Requests where the caller is not on the youverse network yet holds an xtoken for accessing data
        /// </summary>
        //YouAuthExchange = 17,

        /// <summary>
        /// Requests where a caller a YouAuth authenticated or Certificate (via transit) 
        /// </summary>
        YouAuthOrTransitCertificateIdentified = 22,

        /// <summary>
        /// Requests where the caller is marked as connected and holds a connected token
        /// </summary>
        Connected = 33,

        /// <summary>
        /// Requests where the caller is <see cref="SecurityGroupType.Connected"/> and with-in a specified circle.
        /// </summary>
        CircleConnected = 44,

        /// <summary>
        /// Requests where the caller is <see cref="SecurityGroupType.YouAuthOrTransitCertificateIdentified"/> and with-in a specific list
        /// </summary>
        CustomList = 55,

        /// <summary>
        /// Specifies that only the owner can access a file
        /// </summary>
        Owner = 111
    }
}