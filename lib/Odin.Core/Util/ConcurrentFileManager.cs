using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

[assembly: InternalsVisibleTo("Odin.Core.Tests")]

public enum ConcurrentFileLockEnum
{
    ReadLock,
    WriteLock
}

public class ConcurrentFileLock
{
    public ReaderWriterLockSlim Lock = new ReaderWriterLockSlim();
    public int ReferenceCount = 0;
}

public class LockManagedFileStream : FileStream
{
    private readonly ConcurrentFileManager _concurrentFileManagerGlobal;
    private string _path;

    public LockManagedFileStream(string path, FileMode mode, FileAccess access, FileShare share, ConcurrentFileManager lockObj)
        : base(path, mode, access, share)
    {
        _concurrentFileManagerGlobal = lockObj;
        _path = path;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _concurrentFileManagerGlobal.ReleaseLockObjectCounter(_path);
        }
    }
}

public class ConcurrentFileManager
{
    private const int _threadTimeout = 1000;
    internal readonly Dictionary<string, ConcurrentFileLock> _dictionaryLocks = new Dictionary<string, ConcurrentFileLock>();
    private ConcurrentFileLockEnum _type;

    private ConcurrentFileLock ReadyLockObjectCounter(string filePath, ConcurrentFileLockEnum lockType)
    {
        lock (_dictionaryLocks)
        {
            if (!_dictionaryLocks.ContainsKey(filePath))
            {
                _dictionaryLocks[filePath] = new ConcurrentFileLock();
                _type = lockType;
            }

            if (lockType != _type)
                throw new Exception("Cannot mix read and write lock types");

            if (_type == ConcurrentFileLockEnum.ReadLock)
            {
                if (_dictionaryLocks[filePath].Lock.TryEnterReadLock(_threadTimeout) == false)
                    throw new TimeoutException($"Timeout waiting for read lock for file {filePath}");
            }
            else if (_type == ConcurrentFileLockEnum.WriteLock)
            {
                if (_dictionaryLocks[filePath].Lock.TryEnterWriteLock(_threadTimeout) == false)
                    throw new TimeoutException($"Timeout waiting for write lock for file {filePath}");
            }

            _dictionaryLocks[filePath].ReferenceCount++;
            return _dictionaryLocks[filePath];
        }
    }

    /// <summary>
    /// Don't use this except from the ConcurrentFileLock class
    /// </summary>
    /// <param name="filePath"></param>
    public void ReleaseLockObjectCounter(string filePath)
    {
        lock (_dictionaryLocks)
        {
            if (_dictionaryLocks.ContainsKey(filePath))
            {
                _dictionaryLocks[filePath].ReferenceCount--;

                if (_type == ConcurrentFileLockEnum.ReadLock)
                    _dictionaryLocks[filePath].Lock.ExitReadLock();

                if (_type == ConcurrentFileLockEnum.WriteLock)
                    _dictionaryLocks[filePath].Lock.ExitWriteLock();

                if (_dictionaryLocks[filePath].ReferenceCount == 0)
                    _dictionaryLocks.Remove(filePath);
            }
        }
    }

    public void ReadFile(string filePath, Action<string> readAction)
    {
        var fileLock = ReadyLockObjectCounter(filePath, ConcurrentFileLockEnum.ReadLock);

        try
        {
            readAction(filePath);
        }
        finally
        {
            ReleaseLockObjectCounter(filePath);
        }
    }

    public Stream ReadStream(string filePath)
    {
        var fileLock = ReadyLockObjectCounter(filePath, ConcurrentFileLockEnum.ReadLock);

        try
        {
            // Create and return the custom stream that manages the lock
            return new LockManagedFileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, this);
        }
        catch
        {
            // If an error occurs, make sure to exit the read lock before throwing the exception
            ReleaseLockObjectCounter(filePath);
            throw;
        }
        // Note: Lock release is managed by the LockManagedFileStream when it is disposed
    }


    public void WriteFile(string filePath, Action<string> writeAction)
    {
        var fileLock = ReadyLockObjectCounter(filePath, ConcurrentFileLockEnum.WriteLock);

        try
        {
            writeAction(filePath);
        }
        finally
        {
            ReleaseLockObjectCounter(filePath);
        }
    }

    public void DeleteFile(string filePath)
    {
        var fileLock = ReadyLockObjectCounter(filePath, ConcurrentFileLockEnum.WriteLock);

        try
        {
            File.Delete(filePath);
        }
        finally
        {
            ReleaseLockObjectCounter(filePath);
        }
    }

    public void MoveFile(string sourcePath, string destinationPath, Action<string, string> moveAction)
    {
        ConcurrentFileLock sourceLock = null, destinationLock = null;

        // Lock destination first to avoid deadlocks
        destinationLock = ReadyLockObjectCounter(destinationPath, ConcurrentFileLockEnum.WriteLock);

        try
        {
            sourceLock = ReadyLockObjectCounter(sourcePath, ConcurrentFileLockEnum.WriteLock);
            try
            {
                moveAction(sourcePath, destinationPath);
            }
            finally
            {
                ReleaseLockObjectCounter(sourcePath);
            }
        }
        finally
        {
            ReleaseLockObjectCounter(destinationPath);
        }
    }
}