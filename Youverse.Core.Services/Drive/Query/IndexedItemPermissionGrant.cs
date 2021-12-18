using System;
using Youverse.Core.Services.Drive.Security;

namespace Youverse.Core.Services.Drive.Query
{
    public class IndexedItemPermissionGrant
    {
        public Guid FileId { get; set; }
        
        public Guid GranteeId { get; set; }
        
        public string DomainIdentity { get; set; }
        
        public Permission Permission { get; set; }
    }
}