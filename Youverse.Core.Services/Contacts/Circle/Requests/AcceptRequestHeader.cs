using System.Collections.Generic;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Contacts.Circle.Requests;

public class AcceptRequestHeader
{
    public string Sender { get; set; }
        
    /// <summary>
    /// The drives which should be accessible to the recipient of this request
    /// </summary>
    public IEnumerable<DriveGrantRequest> Drives { get; set; }
        
    /// <summary>
    /// The permissions which should be granted to the recipient
    /// </summary>
    public PermissionSet Permissions { get; set; }
}