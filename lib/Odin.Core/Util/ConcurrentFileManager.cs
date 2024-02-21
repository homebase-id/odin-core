using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;

[assembly: InternalsVisibleTo("Odin.Core.Tests")]

namespace Odin.Core.Util;

public class LockConflictException : OdinSystemException
{
    public string FilePath { get; private set; }
    public ConcurrentFileLockEnum RequestedLockType { get; private set; }
    public ConcurrentFileLockEnum ExistingLockType { get; private set; }
    public string LockingInfo { get; private set; }
    public int ReferenceCount { get; private set; }

    public LockConflictException(string message, string filePath, ConcurrentFileLockEnum requestedLockType, ConcurrentFileLockEnum existingLockType, string debugInfo, int referenceCount)
        : base(message)
    {
        FilePath = filePath;
        RequestedLockType = requestedLockType;
        ExistingLockType = existingLockType;
        LockingInfo = debugInfo;
        ReferenceCount = referenceCount;
    }
}


public class ConcurrentFileManager
{
    private readonly ILogger<ConcurrentFileManager> _logger;
    private readonly ICorrelationContext _correlationContext;
    public readonly string _file;
    public readonly int _line;

    public ConcurrentFileManager(ILogger<ConcurrentFileManager> logger, ICorrelationContext correlationContext, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1)
    {
        _logger = logger;
        _correlationContext = correlationContext;
        _file = file;
        _line = line;
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
        lock (_dictionaryLocks)
        {
            if (!_dictionaryLocks.ContainsKey(filePath))
            {
                _dictionaryLocks[filePath] = new ConcurrentFileLock(lockType);
                _dictionaryLocks[filePath].ReferenceCount = 1;

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _dictionaryLocks[filePath].LockingInfo = new LockingInfo()
                    {
                        CorrelationId = _correlationContext?.Id,
                        ThreadId = Thread.CurrentThread.ManagedThreadId,
                        LockType = lockType,
                        CallStack = GetCallStack()
                    };
                }

                LogLockStackTrace(filePath, lockType, 1);
                _dictionaryLocks[filePath].Lock.Wait();
                return;
            }

            fileLock = _dictionaryLocks[filePath];

            if (lockType != fileLock.Type)
            {
                string lockingInfo = fileLock.LockingInfo?.ToString() ?? "Enable verbose logging to see locking info";
                string message = $"No access, file is already being {(fileLock.Type == ConcurrentFileLockEnum.ReadLock ? "read":"written")} by another thread." +
                                 $"\nRequested Lock Type:[{lockType}]" +
                                 $"\n{lockingInfo}" +
                                 $"\nReference Count:[{_dictionaryLocks[filePath].ReferenceCount}]" +
                                 $"\nFile:[{filePath}]";
                throw new LockConflictException(message, filePath, lockType, fileLock.Type, lockingInfo, _dictionaryLocks[filePath].ReferenceCount);
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
        _logger.LogTrace("ReadFile Lock requested on file {filePath}", filePath);
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
        _logger.LogTrace("ReadStream Lock requested on file {filePath}", filePath);
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
        _logger.LogTrace("WriteFile Lock requested on file {filePath}", filePath);
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
        _logger.LogTrace("DeleteFile Lock requested on file {filePath}", filePath);
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
        _logger.LogTrace("MoveFile Lock requested on source file {sourcePath}", sourcePath);
        // Lock destination first to avoid deadlocks
        EnterLock(destinationPath, ConcurrentFileLockEnum.WriteLock);

        try
        {
            _logger.LogTrace("MoveFile Lock requested on destination file {destinationPath}", destinationPath);
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

    private void LogLockStackTrace(string filePath, ConcurrentFileLockEnum lockType, int referenceCount)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            var methods = GetCallStack();
            var threadId = Thread.CurrentThread.ManagedThreadId;
            _logger.LogTrace(
                "\n\nLock\n\tThreadId:{threadId} \n\tLockType:{lockType} \n\tFile path [{filePath}]\n\tReference Count: [{referenceCount}]\n\tStack:[{methods}]\n\n",
                threadId, lockType, filePath, referenceCount, methods);
        }
    }

    private void LogUnlockStackTrace(string filePath)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            var methods = GetCallStack();
            var threadId = Thread.CurrentThread.ManagedThreadId;
            _logger.LogTrace(
                "\n\nUnlock\n\tThreadId:{threadId} \n\tFile path [{filePath}]\n\tStack:[{methods}]\n\n",
                threadId, filePath, methods);
        }
    }

    private string GetCallStack()
    {
        if (!_logger.IsEnabled(LogLevel.Trace))
        {
            return string.Empty;
        }
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