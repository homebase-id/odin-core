using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Odin.Core.Logging.CorrelationId;
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
    private readonly ICorrelationContext _correlationContext;

    public ConcurrentFileManager()
    {
    }

    public ConcurrentFileManager(ICorrelationContext correlationContext)
    {
        _correlationContext = correlationContext;
    }

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

        public LockingInfo LockingInfo { get; set; }
    }

    internal class LockingInfo
    {
        public int ThreadId { get; set; }
        public string CorrelationId { get; set; }
        public string CallStack { get; set; }
        public ConcurrentFileLockEnum LockType { get; set; }

        public override string ToString()
        {
            return $"Locking Info\n" +
                   $"\nCorrelationId: {CorrelationId}" +
                   $"\nThreadId: {ThreadId}" +
                   $"\nLockType: {LockType}" +
                   $"\nCall Stack:" +
                   $"\n{CallStack}\n\n";
        }
    }

    //private int _debugCount = 42;
    //private StringBuilder _sb = new StringBuilder();

    private const int _threadTimeout = 1000;
    internal readonly Dictionary<string, ConcurrentFileLock> _dictionaryLocks = new Dictionary<string, ConcurrentFileLock>();


    private void EnterLock(string filePath, ConcurrentFileLockEnum lockType)
    {
        ConcurrentFileLock fileLock;

        int referenceCount;
        // I replaced this with more detailed logs in the calling functions
        // Log.Information($"Lock Type requested [{lockType}] on file [{filePath}]");
        //
        lock (_dictionaryLocks)
        {
            if (!_dictionaryLocks.ContainsKey(filePath))
            {
                _dictionaryLocks[filePath] = new ConcurrentFileLock(lockType);
                //_dictionaryLocks[filePath].DebugCount = _debugCount++;
                _dictionaryLocks[filePath].ReferenceCount = 1;

                _dictionaryLocks[filePath].LockingInfo = new LockingInfo()
                {
                    CorrelationId = _correlationContext?.Id,
                    ThreadId = Thread.CurrentThread.ManagedThreadId,
                    LockType = lockType,
                    CallStack = GetCallStack()
                };

                LogLockStackTrace(filePath, lockType, 1);
                _dictionaryLocks[filePath].Lock.Wait();
                return;
            }

            fileLock = _dictionaryLocks[filePath];

            if (lockType != fileLock.Type)
            {
                string message = $"No access, file is already being written or read by another thread." +
                                 $"\nRequested Lock Type:[{lockType}]" +
                                 $"\n{fileLock.LockingInfo}" +
                                 $"\nReference Count:[{_dictionaryLocks[filePath].ReferenceCount}]" +
                                 $"\nFile:[{filePath}]";
                throw new Exception(message);
            }

            // Optimistically increase the reference count
            _dictionaryLocks[filePath].ReferenceCount++;
            referenceCount = fileLock.ReferenceCount;
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
        else
            LogLockStackTrace(filePath, lockType, referenceCount);
    }
    
    /// <summary>
    /// Don't use this except from here and the LockManagedFileStream class' Dispose
    /// </summary>
    /// <param name="filePath"></param>
    internal void ExitLock(string filePath)
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
        Log.Information($"ReadFile Lock requested on file [{filePath}]");
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
        Log.Information($"ReadStream Lock requested on file [{filePath}]");
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
        Log.Information($"WriteFile Lock requested on file [{filePath}]");
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
        Log.Information($"DeleteFile Lock requested on file [{filePath}]");
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
        Log.Information($"MoveFile Lock requested on source file [{sourcePath}]");
        // Lock destination first to avoid deadlocks
        EnterLock(destinationPath, ConcurrentFileLockEnum.WriteLock);

        try
        {
            Log.Information($"MoveFile Lock requested on destination file [{destinationPath}]");
            EnterLock(sourcePath, ConcurrentFileLockEnum.WriteLock);
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

    private static void LogLockStackTrace(string filePath, ConcurrentFileLockEnum lockType, int referenceCount)
    {
        StackTrace stackTrace = new StackTrace(true);
        var methods = GetCallStack();
        var threadId = Thread.CurrentThread.ManagedThreadId;
        Log.Information(
            $"\n\nLock\n\tThreadId:{threadId} \n\tLockType:{lockType} \n\tFile path [{filePath}]\n\tReference Count: [{referenceCount}]\n\tStack:[{methods}]\n\n");
    }

    private static void LogUnlockStackTrace(string filePath)
    {
        var threadId = Thread.CurrentThread.ManagedThreadId;
        Log.Information($"\n\nUnlock\n\tThreadId:{threadId} \n\tFile path [{filePath}]\n\tStack:[{GetCallStack()}]\n\n");
    }
    
    private static string GetCallStack()
    {
        var ignoreList = new List<string>()
        {
            "GetCallStack",
            "LogLockStackTrace",
            "LogUnlockStackTrace",
            "lambda",
            "InvokeCore",
            "Invoke",
            "InvokeNextActionFilterAsync",
            "Next",
            "MoveNext",
            "Dispatch",
            "RunContinuations",
            "StartCallback",
            "WorkerThreadStart",
            "ExecuteWithThreadLocal",
            "TrySetResult",
            "SetExistingTaskResult",
            "SetResult",
            "RunOrScheduleAction",
            "ProcessRequestsAsync",
            "WorkerThreadStart",
            "Run",
            "StartCallback",
            "Execute",
            "Start"
        };
        return string.Join(" -> ", new StackTrace(true).GetFrames()
            .Where(f =>
            {
                var method = f.GetMethod()?.Name ?? "";
                var excludeMethod = ignoreList.Any(text => method.Contains(text));
                return !excludeMethod;
            })
            .Select(f => f.GetMethod()?.Name ?? "No method name"));
    }

}