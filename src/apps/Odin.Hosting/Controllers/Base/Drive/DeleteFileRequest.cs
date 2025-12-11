using System.Collections.Generic;
using Odin.Core.Storage;
using Odin.Services.Drives;

namespace Odin.Hosting.Controllers.Base.Drive;

public class DeleteFileRequest: FileSystemTypeBase
{
    /// <summary>
    /// The file to be deleted
    /// </summary>
    public ExternalFileIdentifier File { get; set; }

    /// <summary>
    /// List of recipients to receive the delete-file notification
    /// </summary>
    public List<string> Recipients { get; set; }
}

/// <summary>
/// Patience @seb, we'll get rid of it. one step at a time :)
/// </summary>
public class FileSystemTypeBase
{
    
    public FileSystemType FileSystemType { get; init; }
    
}