using System;
using System.IO;
using Microsoft.Win32.SafeHandles;
using Odin.Core.Services.Peer;
using Serilog;
using Serilog.Core;

namespace Odin.Core.Services.Drives.DriveCore.Storage;

/// <summary>
/// Debugging class to ensure .close is called via http
/// </summary>
public class OdinFilestream : FileStream
{
    public OdinFilestream(SafeFileHandle handle, FileAccess access) : base(handle, access)
    {
    }

    public OdinFilestream(SafeFileHandle handle, FileAccess access, int bufferSize) : base(handle, access, bufferSize)
    {
    }

    public OdinFilestream(SafeFileHandle handle, FileAccess access, int bufferSize, bool isAsync) : base(handle, access, bufferSize, isAsync)
    {
    }

    [Obsolete("Obsolete")]
    public OdinFilestream(IntPtr handle, FileAccess access) : base(handle, access)
    {
    }

    [Obsolete("Obsolete")]
    public OdinFilestream(IntPtr handle, FileAccess access, bool ownsHandle) : base(handle, access, ownsHandle)
    {
    }

    [Obsolete("Obsolete")]
    public OdinFilestream(IntPtr handle, FileAccess access, bool ownsHandle, int bufferSize) : base(handle, access, ownsHandle, bufferSize)
    {
    }

    [Obsolete("Obsolete")]
    public OdinFilestream(IntPtr handle, FileAccess access, bool ownsHandle, int bufferSize, bool isAsync) : base(handle, access, ownsHandle, bufferSize, isAsync)
    {
    }

    public OdinFilestream(string path, FileMode mode) : base(path, mode)
    {
    }

    public OdinFilestream(string path, FileMode mode, FileAccess access) : base(path, mode, access)
    {
    }

    public OdinFilestream(string path, FileMode mode, FileAccess access, FileShare share) : base(path, mode, access, share)
    {
    }

    public OdinFilestream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize) : base(path, mode, access, share, bufferSize)
    {
    }

    public OdinFilestream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, bool useAsync) : base(path, mode, access, share, bufferSize, useAsync)
    {
    }

    public OdinFilestream(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options) : base(path, mode, access, share, bufferSize, options)
    {
    }

    public OdinFilestream(string path, FileStreamOptions options) : base(path, options)
    {
    }

    public override void Close()
    {
        Log.Logger.Information("Filestream.Close automatically called on filestream");
        base.Close();
    }

    protected override void Dispose(bool disposing)
    {
        Log.Logger.Information("Filestream.Dispose automatically called on filestream");
        base.Dispose(disposing);
    }
}