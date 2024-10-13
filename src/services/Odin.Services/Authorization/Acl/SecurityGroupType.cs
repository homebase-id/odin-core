namespace Odin.Services.Authorization.Acl
{

    public enum SecurityGroupType
    {
        /// <summary>
        /// Indicates anyone on the internet (i.e. public)
        /// </summary>
        Anonymous = 111,

        // TODO: Requests where the caller is not on the odin network yet holds an x-token for accessing data
        //YouAuthExchange = 333,

        /// <summary>
        /// Requests where a caller a YouAuth authenticated or Certificate (via transit) 
        /// </summary>
        Authenticated = 444,

        /// <summary>
        /// Caller is auto-connected and can only write to designated drives
        /// </summary>
        AutoConnected = 555,

        /// <summary>
        /// Requests where the caller is marked as connected and holds a connected token
        /// </summary>
        Connected = 777,
        
        /// <summary>
        /// Specifies that only the owner can access a file
        /// </summary>
        Owner = 999,
        
        System = 1 
    }
}