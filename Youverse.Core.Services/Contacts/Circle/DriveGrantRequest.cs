using System;
using System.Collections.Generic;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Contacts.Circle;

public class DriveGrantRequest
{
    public TargetDrive Drive { get; set; }

    /// <summary>
    /// The type of access allowed for this drive grant
    /// </summary>
    public DrivePermission Permission { get; set; }
}