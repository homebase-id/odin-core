using System;

namespace Youverse.Core.Services.Drive.Security
{
    public class PermissionGrant
    {
        /// <summary>
        /// The Id of the domain being granted access
        /// </summary>
        public Guid GranteeId { get; set; }
        
        /// <summary>
        /// The file receiving the permission
        /// </summary>
        public Guid FileId { get; set; }
        
        /// <summary>
        /// The permission which has been granted
        /// </summary>
        public Permission Permission { get; set; }
        
        public UInt64 LastUpdatedTimestamp { get; set; }
        
    }
}