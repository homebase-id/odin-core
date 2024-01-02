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
    private readonly ConcurrentFileLock _lock;

    public LockManagedFileStream(string path, FileMode mode, FileAccess access, FileShare share, ConcurrentFileLock lockObj)
        : base(path, mode, access, share)
    {
        _lock = lockObj;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            // Release the read lock when the stream is disposed
            if (_lock.Lock.IsReadLockHeld)
            {
                _lock.Lock.ExitReadLock();
                _lock.Decrement();
            }
        }
    }
}


public class ConcurrentFileManager
{
    private const int threadTimeout = 1000;
    private readonly Dictionary<string, ConcurrentFileLock> locks = new Dictionary<string, ConcurrentFileLock>();

    private ConcurrentFileLock GetLock(string filePath)
    {
        lock (locks)
        {
            if (!locks.ContainsKey(filePath))
            {
                locks[filePath] = new ConcurrentFileLock();
            }
            locks[filePath].Increment();
            return locks[filePath];
        }
    }

    private void ReleaseLock(string filePath)
    {
        lock (locks)
        {
            if (locks.ContainsKey(filePath))
            {
                locks[filePath].Decrement();
                if (locks[filePath].ReferenceCount == 0)
                {
                    locks.Remove(filePath);
                }
            }
        }
    }

    public void ReadFile(string filePath, Action<string> readAction)
    {
        var fileLock = GetLock(filePath);

        if (fileLock.Lock.TryEnterReadLock(threadTimeout) == false)
            throw new TimeoutException($"Timeout waiting for ReadFile() read lock for file {filePath}");

        try
        {
            readAction(filePath);
        }
        finally
        {
            fileLock.Lock.ExitReadLock();
            ReleaseLock(filePath);
        }
    }

    public Stream ReadStream(string filePath)
    {
        var fileLock = GetLock(filePath);
        if (fileLock.Lock.TryEnterReadLock(threadTimeout) == false)
            throw new TimeoutException($"Timeout waiting for ReadStream() read lock for file {filePath}");

        try
        {
            // Create and return the custom stream that manages the lock
            return new LockManagedFileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, fileLock);
        }
        catch
        {
            // If an error occurs, make sure to exit the read lock before throwing the exception
            if (fileLock.Lock.IsReadLockHeld)
            {
                fileLock.Lock.ExitReadLock();
            }
            ReleaseLock(filePath);
            throw;
        }
        // Note: Lock release is managed by the LockManagedFileStream when it is disposed
    }


    public void WriteFile(string filePath, Action<string> writeAction)
    {
        var fileLock = GetLock(filePath);
        if (fileLock.Lock.TryEnterWriteLock(threadTimeout) == false)
            throw new TimeoutException($"Timeout waiting for WriteFile() write lock for file {filePath}");

        try
        {
            writeAction(filePath);
        }
        finally
        {
            fileLock.Lock.ExitWriteLock();
            ReleaseLock(filePath);
        }
    }

    public void DeleteFile(string filePath)
    {
        var fileLock = GetLock(filePath);
        if (fileLock.Lock.TryEnterWriteLock(threadTimeout) == false)
            throw new TimeoutException($"Timeout waiting for DeleteFile() write lock for file {filePath}");
        try
        {
            File.Delete(filePath);
        }
        finally
        {
            fileLock.Lock.ExitWriteLock();
            ReleaseLock(filePath);
        }
    }
    public void MoveFile(string sourcePath, string destinationPath, Action<string, string> moveAction)
    {
        ConcurrentFileLock sourceLock = null, destinationLock = null;

        // Lock destination first to avoid deadlocks
        destinationLock = GetLock(destinationPath);
        if (destinationLock.Lock.TryEnterWriteLock(threadTimeout) == false)
            throw new TimeoutException($"Timeout waiting for MoveFile() destiation file write lock for file {destinationPath}");

        try
        {
            sourceLock = GetLock(sourcePath);

            if (sourceLock.Lock.TryEnterWriteLock(threadTimeout) == false)
                throw new TimeoutException($"Timeout waiting for MoveFile() source file write lock for file {sourcePath}");

            // Perform the move operation
            moveAction(sourcePath, destinationPath);
        }
        finally
        {
            if (sourceLock.Lock.IsWriteLockHeld)
            {
                sourceLock.Lock.ExitWriteLock();
                ReleaseLock(sourcePath);
            }

            destinationLock.Lock.ExitWriteLock();
            ReleaseLock(destinationPath);
        }
    }
}