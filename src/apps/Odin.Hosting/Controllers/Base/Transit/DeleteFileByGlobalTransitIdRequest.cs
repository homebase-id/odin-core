using System.Collections.Generic;
using Odin.Services.Drives;
using Odin.Core.Storage;

namespace Odin.Hosting.Controllers.Base.Transit;

public class DeleteFileByGlobalTransitIdRequest
{
    public FileSystemType FileSystemType { get; set; }

    /// <summary>
    /// The file to be deleted
    /// </summary>
    public GlobalTransitIdFileIdentifier GlobalTransitIdFileIdentifier { get; set; }

    /// <summary>
    /// List of recipients to receive the delete-file notification
    /// </summary>
    public List<string> Recipients { get; set; }
}