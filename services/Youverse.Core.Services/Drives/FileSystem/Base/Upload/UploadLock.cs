using System;
using System.Collections.Concurrent;

namespace Youverse.Core.Services.Drives.FileSystem.Base.Upload;

public class UploadLock
{
    public UploadLock()
    {
        Locks = new ConcurrentDictionary<Guid, Guid>();
    }

    public ConcurrentDictionary<Guid, Guid> Locks { get; }
}