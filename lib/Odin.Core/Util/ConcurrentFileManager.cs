using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

public class ConcurrentFileManager
{
    private const int threadTimeout = 1000;
    private class FileLock
    {
        public ReaderWriterLockSlim Lock = new ReaderWriterLockSlim();
        public int ReferenceCount = 0;
    }

    private readonly Dictionary<string, FileLock> locks = new Dictionary<string, FileLock>();

    private FileLock GetLock(string filePath)
    {
        lock (locks)
        {
            if (!locks.ContainsKey(filePath))
            {
                locks[filePath] = new FileLock();
            }
            locks[filePath].ReferenceCount++;
            return locks[filePath];
        }
    }

    private void ReleaseLock(string filePath)
    {
        lock (locks)
        {
            if (locks.ContainsKey(filePath))
            {
                locks[filePath].ReferenceCount--;
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
        FileLock sourceLock = null, destinationLock = null;

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