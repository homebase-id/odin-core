using System;
using System.IO;
using static System.Net.WebRequestMethods;
using System.Numerics;

namespace Odin.Core.Util;

public class LockManagedFileStream : FileStream
{
    private readonly ConcurrentFileManager _concurrentFileManagerGlobal;
    private string _path;
    private bool _isDisposed = false;


    public LockManagedFileStream(string path, FileMode mode, FileAccess access, FileShare share, ConcurrentFileManager lockObj)
        : base(path, mode, access, share)
    {
        _concurrentFileManagerGlobal = lockObj;
        _path = path;
    }

    ~LockManagedFileStream()
    {
        if (!_isDisposed)
        {
#if DEBUG
            throw new Exception($"ManagedFileStream was not disposed properly {_path}.");
#else
           Serilog.Log.Error($"ManagedFileStream was not disposed properly {_path}.");
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