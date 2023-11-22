using System;
using System.Collections.Generic;
using Odin.Core.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive;

public class DeleteFileByGroupIdRequest
{
    /// <summary>
    /// The groupId of all files to be deleted
    /// </summary>
    public Guid GroupId { get; set; }

    public TargetDrive TargetDrive { get; set; }

    /// <summary>
    /// List of recipients to receive the delete-file notification
    /// </summary>
    public List<string> Recipients { get; set; }
}
