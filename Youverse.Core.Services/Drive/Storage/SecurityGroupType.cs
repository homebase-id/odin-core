namespace Youverse.Core.Services.Drive.Storage
{
    public enum SecurityGroupType
    {
        /// <summary>
        /// Indicates anyone on the internet (i.e. public)
        /// </summary>
        Anonymous = 11,
        
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
        CustomList = 55
    }
}