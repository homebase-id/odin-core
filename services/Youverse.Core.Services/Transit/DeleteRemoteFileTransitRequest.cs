using System;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Storage;

namespace Youverse.Core.Services.Transit;

public class DeleteRemoteFileTransitRequest
{
    public TargetDrive TargetDrive { get; set; }
    public Guid GlobalTransitId { get; set; }
    
    public FileSystemType FileSystemType { get; set; }
}