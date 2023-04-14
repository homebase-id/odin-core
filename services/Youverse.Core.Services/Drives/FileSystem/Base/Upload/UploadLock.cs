using System;
using System.Collections.Concurrent;
using Youverse.Core.Exceptions;

namespace Youverse.Core.Services.Drives.FileSystem.Base.Upload;

public class UploadLock
{
    private readonly ConcurrentDictionary<Guid, Guid> _locks;

    public UploadLock()
    {
        _locks = new ConcurrentDictionary<Guid, Guid>();
    }
    
    public void LockOrFail(InternalDriveFileId file)
    {
        if (!_locks.TryAdd(CreateKey(file), Guid.Empty))
        {
            throw new YouverseClientException("File is locked", YouverseClientErrorCode.UploadedFileLocked);
        }
    }

    public void ReleaseLock(InternalDriveFileId file)
    {
        _locks.TryRemove(CreateKey(file), out var _);
    }

    private Guid CreateKey(InternalDriveFileId file)
    {
        var key = new Guid(ByteArrayUtil.EquiByteArrayXor(file.FileId.ToByteArray(), file.DriveId.ToByteArray()));
        return key;
    }
}