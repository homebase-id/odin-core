using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Util;

public class LockManagedFileStream : FileStream
{
    private readonly ILogger _logger;
    private readonly ConcurrentFileManager _concurrentFileManagerGlobal;
    private string _path;
    private bool _isDisposed = false;


    public LockManagedFileStream(ILogger logger, string path, FileMode mode, FileAccess access, FileShare share, ConcurrentFileManager lockObj)
        : base(path, mode, access, share)
    {
        _concurrentFileManagerGlobal = lockObj;
        _logger = logger;
        _path = path;
    }

    ~LockManagedFileStream()
    {
        if (!_isDisposed)
        {
#if DEBUG
            throw new Exception($"ManagedFileStream created by ConcurrentFileManager (instantiated {_concurrentFileManagerGlobal._file} line {_concurrentFileManagerGlobal._line}) was not disposed properly {_path}.");
#else
           _logger.LogError($"ManagedFileStream created by ConcurrentFileManager (instantiated {_concurrentFileManagerGlobal._file} line {_concurrentFileManagerGlobal._line}) was not disposed properly {_path}.");
#endif
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                _concurrentFileManagerGlobal.ExitLock(_path);
            }

            _isDisposed = true;
        }

        base.Dispose(disposing);
    }
}