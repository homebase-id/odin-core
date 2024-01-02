using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

public class ConcurrentFileLock
{
    public ReaderWriterLockSlim Lock = new ReaderWriterLockSlim();
    public int ReferenceCount = 0;

    public void Increment()
    { 
        lock (Lock)
        {
            ReferenceCount++;
        }
    }

    public void Decrement()
    {
        lock (Lock)
        {
            ReferenceCount--;
        }
    }
}

public class LockManagedFileStream : FileStream
{
    private readonly ConcurrentFileManager _dictionaryLocks;
    private string _path;

    public LockManagedFileStream(string path, FileMode mode, FileAccess access, FileShare share, ConcurrentFileManager lockObj)
        : base(path, mode, access, share)
    {
        _dictionaryLocks = lockObj;
        _path = path;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _dictionaryLocks.ReleaseLockObject(_path, true);
        }
    }
}


public class ConcurrentFileManager
{
    private const int _threadTimeout = 1000;
    private readonly Dictionary<string, ConcurrentFileLock> _dictionaryLocks = new Dictionary<string, ConcurrentFileLock>();

    private ConcurrentFileLock ReadyLockObject(string filePath)
    {
        lock (_dictionaryLocks)
        {
            if (!_dictionaryLocks.ContainsKey(filePath))
            {
                _dictionaryLocks[filePath] = new ConcurrentFileLock();
            }
            _dictionaryLocks[filePath].Increment();
            return _dictionaryLocks[filePath];
        }
    }

    /// <summary>
    /// Don't use this except from the ConcurrentFileLock class
    /// </summary>
    /// <param name="filePath"></param>
    public void ReleaseLockObject(string filePath, bool exitReadLock)
    {
        lock (_dictionaryLocks)
        {
            if (_dictionaryLocks.ContainsKey(filePath))
            {
                _dictionaryLocks[filePath].Decrement();

                if (exitReadLock)
                    _dictionaryLocks[filePath].Lock.ExitReadLock();

                if (_dictionaryLocks[filePath].ReferenceCount == 0)
                {
                    _dictionaryLocks.Remove(filePath);
                }

            }
        }
    }

    public void ReadFile(string filePath, Action<string> readAction)
    {
        var fileLock = ReadyLockObject(filePath);

        if (fileLock.Lock.TryEnterReadLock(_threadTimeout) == false)
        {
            ReleaseLockObject(filePath, false);
            throw new TimeoutException($"Timeout waiting for ReadFile() read lock for file {filePath}");
        }

        try
        {
            readAction(filePath);
        }
        finally
        {
            fileLock.Lock.ExitReadLock();
            ReleaseLockObject(filePath, false);
        }
    }

    public Stream ReadStream(string filePath)
    {
        var fileLock = ReadyLockObject(filePath);
        if (fileLock.Lock.TryEnterReadLock(_threadTimeout) == false)
        {
            ReleaseLockObject(filePath, false);
            throw new TimeoutException($"Timeout waiting for ReadStream() read lock for file {filePath}");
        }

        try
        {
            // Create and return the custom stream that manages the lock
            return new LockManagedFileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, this);
        }
        catch
        {
            // If an error occurs, make sure to exit the read lock before throwing the exception
            fileLock.Lock.ExitReadLock();
            ReleaseLockObject(filePath, false);
            throw;
        }
        // Note: Lock release is managed by the LockManagedFileStream when it is disposed
    }


    public void WriteFile(string filePath, Action<string> writeAction)
    {
        var fileLock = ReadyLockObject(filePath);
        if (fileLock.Lock.TryEnterWriteLock(_threadTimeout) == false)
        {
            ReleaseLockObject(filePath, false);
            throw new TimeoutException($"Timeout waiting for WriteFile() write lock for file {filePath}");
        }

        try
        {
            writeAction(filePath);
        }
        finally
        {
            fileLock.Lock.ExitWriteLock();
            ReleaseLockObject(filePath, false);
        }
    }

    public void DeleteFile(string filePath)
    {
        var fileLock = ReadyLockObject(filePath);
        if (fileLock.Lock.TryEnterWriteLock(_threadTimeout) == false)
        {
            ReleaseLockObject(filePath, false);
            throw new TimeoutException($"Timeout waiting for DeleteFile() write lock for file {filePath}");
        }

        try
        {
            File.Delete(filePath);
        }
        finally
        {
            fileLock.Lock.ExitWriteLock();
            ReleaseLockObject(filePath, false);
        }
    }

    public void MoveFile(string sourcePath, string destinationPath, Action<string, string> moveAction)
    {
        ConcurrentFileLock sourceLock = null, destinationLock = null;

        // Lock destination first to avoid deadlocks
        destinationLock = ReadyLockObject(destinationPath);
        if (destinationLock.Lock.TryEnterWriteLock(_threadTimeout) == false)
        {
            ReleaseLockObject(destinationPath, false);
            throw new TimeoutException($"Timeout waiting for MoveFile() destiation file write lock for file {destinationPath}");
        }

        try
        {
            sourceLock = ReadyLockObject(sourcePath);

            if (sourceLock.Lock.TryEnterWriteLock(_threadTimeout) == false)
            {
                destinationLock.Lock.ExitWriteLock();
                ReleaseLockObject(destinationPath, false);
                ReleaseLockObject(sourcePath, false);
                throw new TimeoutException($"Timeout waiting for MoveFile() source file write lock for file {sourcePath}");
            }

            // Perform the move operation
            moveAction(sourcePath, destinationPath);
        }
        finally
        {
            sourceLock.Lock.ExitWriteLock();
            destinationLock.Lock.ExitWriteLock();
            ReleaseLockObject(destinationPath, false);
            ReleaseLockObject(sourcePath, false);
        }
    }
}