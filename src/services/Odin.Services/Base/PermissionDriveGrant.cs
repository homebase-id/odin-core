using System.Collections.Generic;
using System.Linq;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;

namespace Odin.Services.Base
{
    /// <summary>
    /// Indicates a set of permissions being requested
    /// </summary>
    public class PermissionSetGrantRequest
    {
        /// <summary>
        /// The drives being requested
        /// </summary>
        public IEnumerable<DriveGrantRequest> Drives { get; set; }

        /// <summary>
        /// The permissions being requested
        /// </summary>
        public PermissionSet PermissionSet { get; set; }

        //removed as it makes the API interface wonky
        // public bool IsValid()
        // {
        //     // you must request at least 1 drive or 1 permission
        //     var driveList = this.Drives?.ToList() ?? [];
        //     
        //     var driveGrantsValid = driveList.Count == 0 || driveList.TrueForAll(dgr => dgr.PermissionedDrive.Drive.IsValid());
        //     
        //     var hasAtLeastOneRequest = this.PermissionSet?.Keys?.Count > 0  || driveList.Count > 1;
        //     return driveGrantsValid && hasAtLeastOneRequest;
        // }
    }
}