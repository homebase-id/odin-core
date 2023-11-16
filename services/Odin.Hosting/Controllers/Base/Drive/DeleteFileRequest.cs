using System.Collections.Generic;
using Odin.Core.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive;

public class DeleteFileRequest
{
    /// <summary>
    /// The file to be deleted
    /// </summary>
    public ExternalFileIdentifier File { get; set; }

    /// <summary>
    /// If the file has a GlobalTransitId, all Recipients will receive a notification to delete the file
    /// </summary>
    public bool DeleteLinkedFiles { get; set; }

    /// <summary>
    /// List of recipients to receive the delete-file notification
    /// </summary>
    public List<string> Recipients { get; set; }
}