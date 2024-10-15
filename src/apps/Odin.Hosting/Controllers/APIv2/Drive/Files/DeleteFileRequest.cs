using System;
using System.Collections.Generic;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.APIv2.Drive.Files;

public class DeleteFileRequestV2
{
    /// <summary>
    /// Files with 
    /// </summary>
    public List<ExternalFileIdentifier> FileIds { get; set; }

    /// <summary>
    /// Files with the one of the groupIds will be deleted
    /// </summary>
    public List<Guid> GroupIds { get; set; }

    /// <summary>
    /// If true, specifies any files matching <see cref="GroupIds"/> or <see cref="FileIds"/> will be hard deleted
    /// </summary>
    public bool UseHardDelete { get; set; }
}