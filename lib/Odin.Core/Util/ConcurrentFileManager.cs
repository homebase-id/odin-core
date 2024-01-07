using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Serilog;

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
        //public int DebugCount;

        public ConcurrentFileLock(ConcurrentFileLockEnum type)
        {
            Type = type;
            if (Type == ConcurrentFileLockEnum.ReadLock)
                Lock = new SemaphoreSlim(100);
            else
                Lock = new SemaphoreSlim(1);
        }
    }

    //private int _debugCount = 42;
    //private StringBuilder _sb = new StringBuilder();

    private const int _threadTimeout = 1000;
    internal readonly Dictionary<string, ConcurrentFileLock> _dictionaryLocks = new Dictionary<string, ConcurrentFileLock>();


    private void EnterLock(string filePath, ConcurrentFileLockEnum lockType)
    {
        ConcurrentFileLock fileLock;

        Log.Information($"Lock Type requested [{lockType}] on file [{filePath}]");
        lock (_dictionaryLocks)
        {
            if (!_dictionaryLocks.ContainsKey(filePath))
            {
                _dictionaryLocks[filePath] = new ConcurrentFileLock(lockType);
                //_dictionaryLocks[filePath].DebugCount = _debugCount++;
                _dictionaryLocks[filePath].ReferenceCount = 1;
                LogLockStackTrace(filePath, lockType);
                _dictionaryLocks[filePath].Lock.Wait();
                return;
            }

            fileLock = _dictionaryLocks[filePath];
            
            if (lockType != fileLock.Type)
                throw new Exception($"No access, file is already being written or read by another thread. \nRequested Lock Type:[{lockType}]\nActual Lock Type:[{fileLock}]\nFile:[{filePath}]");

            // Optimistically increase the reference count
            _dictionaryLocks[filePath].ReferenceCount++;
        }

        if (fileLock.Lock.Wait(_threadTimeout) == false)
        {
            lock (_dictionaryLocks)
            {
                _dictionaryLocks[filePath].ReferenceCount--;

                if (fileLock.ReferenceCount == 0)
                    _dictionaryLocks.Remove(filePath);
            }

            throw new TimeoutException($"Timeout waiting for lock for file {filePath}");
        }
    }

    /// <summary>
    /// Don't use this except from the ConcurrentFileLock class' Dispose
    /// PROBLEM: We'd like to be able to release while a thread is waiting in Enter...
    /// </summary>
    /// <param name="filePath"></param>
    public void ExitLock(string filePath)
    {
        ConcurrentFileLock fileLock;

        lock (_dictionaryLocks)
        {
            if (_dictionaryLocks.ContainsKey(filePath))
            {
                fileLock = _dictionaryLocks[filePath];

                if (fileLock.ReferenceCount < 1)
                    throw new Exception("kapow you called with too small a reference count");

                fileLock.ReferenceCount--;

                if (fileLock.ReferenceCount == 0)
                    _dictionaryLocks.Remove(filePath);
            }
            else
                throw new Exception($"Non existent filePath {filePath}");
        }

        fileLock.Lock.Release();
        LogUnlockStackTrace(filePath);
    }

    public void ReadFile(string filePath, Action<string> readAction)
    {
        EnterLock(filePath, ConcurrentFileLockEnum.ReadLock);

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
        EnterLock(filePath, ConcurrentFileLockEnum.ReadLock);

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
        EnterLock(filePath, ConcurrentFileLockEnum.WriteLock);

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
        EnterLock(filePath, ConcurrentFileLockEnum.WriteLock);

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
        // Lock destination first to avoid deadlocks
        EnterLock(destinationPath, ConcurrentFileLockEnum.WriteLock);

        try
        {
            EnterLock(sourcePath, ConcurrentFileLockEnum.WriteLock);
            try
            {
                // Thread.Sleep(5000);
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

    private static void LogLockStackTrace(string filePath, ConcurrentFileLockEnum lockType)
    {
        StackTrace stackTrace = new StackTrace(true);
        var methods = string.Join(" -> ", stackTrace.GetFrames().Select(f => f.GetMethod()?.Name ?? "No method name"));
        var threadId = Thread.CurrentThread.ManagedThreadId;
        Log.Information($"\n\nLock\n\tThreadId:{threadId} \n\tLockType:{lockType} \n\tFile path [{filePath}]\n\tStack:[{methods}]\n\n");
    }

    private static void LogUnlockStackTrace(string filePath)
    {
        StackTrace stackTrace = new StackTrace(true);
        var methods = string.Join(" -> ", stackTrace.GetFrames().Select(f => f.GetMethod()?.Name ?? "No method name"));

        var threadId = Thread.CurrentThread.ManagedThreadId;
        Log.Information($"\n\nUnlock\n\tThreadId:{threadId} \n\tFile path [{filePath}]\n\tStack:[{methods}]\n\n");
    }
}