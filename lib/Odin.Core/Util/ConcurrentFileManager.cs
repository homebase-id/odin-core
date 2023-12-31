using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Odin.Core.Util;

public class ConcurrentFileManager
{
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
        fileLock.Lock.EnterReadLock();
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
        fileLock.Lock.EnterWriteLock();
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
        fileLock.Lock.EnterWriteLock();
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

        try
        {
            // Lock destination first to avoid deadlocks
            destinationLock = GetLock(destinationPath);
            destinationLock.Lock.EnterWriteLock();

            sourceLock = GetLock(sourcePath);

            // Try to acquire the source lock. If not possible, exit early.
            if (!sourceLock.Lock.TryEnterWriteLock(TimeSpan.FromMilliseconds(100))) // Adjust timeout as necessary
            {
                throw new InvalidOperationException("Cannot acquire lock on source file.");
            }

            // Perform the move operation
            moveAction(sourcePath, destinationPath);
        }
        finally
        {
            // Release locks in reverse order of acquisition
            if (sourceLock != null && sourceLock.Lock.IsWriteLockHeld)
            {
                sourceLock.Lock.ExitWriteLock();
                ReleaseLock(sourcePath);
            }

            if (destinationLock != null)
            {
                destinationLock.Lock.ExitWriteLock();
                ReleaseLock(destinationPath);
            }
        }
    }
}