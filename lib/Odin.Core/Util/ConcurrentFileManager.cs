using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

[assembly: InternalsVisibleTo("Odin.Core.Tests")]

public enum ConcurrentFileLockEnum
{
    ReadLock,
    WriteLock
}

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

public class ConcurrentFileManager
{
    internal class ConcurrentFileLock
    {
        public SemaphoreSlim Lock;
        public int ReferenceCount = 0;
        public readonly ConcurrentFileLockEnum Type;
        public int DebugCount;

        public ConcurrentFileLock(ConcurrentFileLockEnum type)
        {
            Type = type;
            if (Type == ConcurrentFileLockEnum.ReadLock)
                Lock = new SemaphoreSlim(100);
            else
                Lock = new SemaphoreSlim(1);
        }
    }

    private int _debugCount = 42;
    private StringBuilder _sb = new StringBuilder();

    private const int _threadTimeout = 1000;
    internal readonly Dictionary<string, ConcurrentFileLock> _dictionaryLocks = new Dictionary<string, ConcurrentFileLock>();


    private ConcurrentFileLock EnterLock(string filePath, ConcurrentFileLockEnum lockType)
    {
        lock (_dictionaryLocks)
        {
            if (!_dictionaryLocks.ContainsKey(filePath))
            {
                _dictionaryLocks[filePath] = new ConcurrentFileLock(lockType);
                _dictionaryLocks[filePath].DebugCount = _debugCount++;
                _sb.AppendLine($"New dictionary entry for i{_dictionaryLocks[filePath].DebugCount}");
            }

            if (lockType != _dictionaryLocks[filePath].Type)
                throw new Exception("Cannot mix read and write lock types");

            if (_dictionaryLocks[filePath].Type == ConcurrentFileLockEnum.ReadLock)
            {
                _sb.AppendLine($"Enter read lock i{_dictionaryLocks[filePath].DebugCount}");

                if (_dictionaryLocks[filePath].Lock.Wait(_threadTimeout) == false)
                    throw new TimeoutException($"Timeout waiting for read lock for file {filePath}");
                _sb.AppendLine($"Made read lock i{_dictionaryLocks[filePath].DebugCount}");
            }
            else if (_dictionaryLocks[filePath].Type == ConcurrentFileLockEnum.WriteLock)
            {
                _sb.AppendLine($"Enter write lock i{_dictionaryLocks[filePath].DebugCount}");
                if (_dictionaryLocks[filePath].Lock.Wait(_threadTimeout) == false)
                    throw new TimeoutException($"Timeout waiting for write lock for file {filePath}");
                _sb.AppendLine($"Made write lock i{_dictionaryLocks[filePath].DebugCount}");
            }

            _dictionaryLocks[filePath].ReferenceCount++;
            _sb.AppendLine($"Lock i{_dictionaryLocks[filePath].DebugCount} locked with count {_dictionaryLocks[filePath].ReferenceCount}");
            return _dictionaryLocks[filePath];
        }
    }

    /// <summary>
    /// Don't use this except from the ConcurrentFileLock class
    /// </summary>
    /// <param name="filePath"></param>
    public void ExitLock(string filePath)
    {
        lock (_dictionaryLocks)
        {
            if (_dictionaryLocks.ContainsKey(filePath))
            {
                if (_dictionaryLocks[filePath].ReferenceCount < 1)
                    throw new Exception("kapow you called with too small a reference count");

                _dictionaryLocks[filePath].ReferenceCount--;

                if (_dictionaryLocks[filePath].Type == ConcurrentFileLockEnum.ReadLock)
                {
                    _sb.AppendLine($"Exiting read lock for i{_dictionaryLocks[filePath].DebugCount} count {_dictionaryLocks[filePath].ReferenceCount}");

                    _dictionaryLocks[filePath].Lock.Release();
                }
                else if (_dictionaryLocks[filePath].Type == ConcurrentFileLockEnum.WriteLock)
                {
                    _sb.AppendLine($"Exiting write lock for i{_dictionaryLocks[filePath].DebugCount} count {_dictionaryLocks[filePath].ReferenceCount}");

                    _dictionaryLocks[filePath].Lock.Release();
                }

                _sb.AppendLine($"Lock i{_dictionaryLocks[filePath].DebugCount} released count {_dictionaryLocks[filePath].ReferenceCount}");

                if (_dictionaryLocks[filePath].ReferenceCount == 0)
                    _dictionaryLocks.Remove(filePath);
            }
            else
                throw new Exception($"Non existent filePath {filePath}");
        }
    }

    public void ReadFile(string filePath, Action<string> readAction)
    {
        var fileLock = EnterLock(filePath, ConcurrentFileLockEnum.ReadLock);

        try
        {
            readAction(filePath);
        }
        finally
        {
            ExitLock(filePath);
        }
    }

    public Stream ReadStream(string filePath)
    {
        var fileLock = EnterLock(filePath, ConcurrentFileLockEnum.ReadLock);

        try
        {
            // Create and return the custom stream that manages the lock
            return new LockManagedFileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, this);
        }
        catch
        {
            // If an error occurs, make sure to exit the read lock before throwing the exception
            ExitLock(filePath);
            throw;
        }

        // Note: Lock release is managed by the LockManagedFileStream when it is disposed
    }


    public void WriteFile(string filePath, Action<string> writeAction)
    {
        var fileLock = EnterLock(filePath, ConcurrentFileLockEnum.WriteLock);

        try
        {
            writeAction(filePath);
        }
        finally
        {
            ExitLock(filePath);
        }
    }

    public void DeleteFile(string filePath)
    {
        var fileLock = EnterLock(filePath, ConcurrentFileLockEnum.WriteLock);

        try
        {
            File.Delete(filePath);
        }
        finally
        {
            ExitLock(filePath);
        }
    }

    public void MoveFile(string sourcePath, string destinationPath, Action<string, string> moveAction)
    {
        ConcurrentFileLock sourceLock = null, destinationLock = null;

        // Lock destination first to avoid deadlocks
        destinationLock = EnterLock(destinationPath, ConcurrentFileLockEnum.WriteLock);

        try
        {
            sourceLock = EnterLock(sourcePath, ConcurrentFileLockEnum.WriteLock);
            try
            {
                moveAction(sourcePath, destinationPath);
            }
            finally
            {
                ExitLock(sourcePath);
            }
        }
        finally
        {
            ExitLock(destinationPath);
        }
    }
}