using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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

    public LockConflictException(string message, string filePath, ConcurrentFileLockEnum requestedLockType, ConcurrentFileLockEnum existingLockType,
        string debugInfo, int referenceCount)
        : base(message)
    {
        FilePath = filePath;
        RequestedLockType = requestedLockType;
        ExistingLockType = existingLockType;
        LockingInfo = debugInfo;
        ReferenceCount = referenceCount;
    }
}

public class ConcurrentFileManager(
    ILogger<ConcurrentFileManager> logger,
    ICorrelationContext correlationContext,
    [CallerFilePath] string file = "",
    [CallerLineNumber] int line = -1)
{
    public readonly string _file = file;
    public readonly int _line = line;

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

    private const int millisecondLoggingThreshold = 100;
    private const int _threadTimeout = 10000;
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

                if (logger.IsEnabled(LogLevel.Trace))
                {
                    _dictionaryLocks[filePath].LockingInfo = new LockingInfo()
                    {
                        CorrelationId = correlationContext?.Id,
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
                string message =
                    $"No access, file is already being {(fileLock.Type == ConcurrentFileLockEnum.ReadLock ? "read" : "written")} by another thread." +
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

        var stopwatch = Stopwatch.StartNew();
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
        {
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > millisecondLoggingThreshold)
            {
                logger.LogDebug($"CFM.EnterLock() waited for {stopwatch.ElapsedMilliseconds}ms for lock on file {filePath}");
            }

            LogLockStackTrace(filePath, lockType, referenceCount);
        }
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

    public Task ReadFile(string filePath, Action<string> readAction, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1)
    {
        logger.LogTrace("ReadFile Lock requested on file {filePath}", filePath);
        EnterLock(filePath, ConcurrentFileLockEnum.ReadLock);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            readAction(filePath);
        }
        finally
        {
            ExitLock(filePath);
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > millisecondLoggingThreshold)
            {
                logger.LogDebug($"CFM.ReadFile() used {stopwatch.ElapsedMilliseconds}ms for file {filePath} called via {file} line {line}");
            }
        }

        return Task.CompletedTask;
    }

    public Task<Stream> ReadStream(string filePath)
    {
        logger.LogTrace("ReadStream Lock requested on file {filePath}", filePath);
        EnterLock(filePath, ConcurrentFileLockEnum.ReadLock);

        try
        {
            // Create and return the custom stream that manages the lock
            var s = new LockManagedFileStream(logger, filePath, FileMode.Open, FileAccess.Read, FileShare.Read, this);
            return Task.FromResult<Stream>(s);
        }
        catch
        {
            // If an error occurs, make sure to exit the read lock before throwing the exception
            ExitLock(filePath);
            throw;
        }

        // Note: Lock release is managed by the LockManagedFileStream when it is disposed
    }

    public async Task WriteFileAsync(string filePath, Func<string, Task> writeAction, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1)
    {
        logger.LogTrace("WriteFile Lock requested on file {filePath}", filePath);
        EnterLock(filePath, ConcurrentFileLockEnum.WriteLock);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await writeAction(filePath);
        }
        finally
        {
            ExitLock(filePath);
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > millisecondLoggingThreshold)
            {
                logger.LogDebug($"CFM.WriteFileAsync() used {stopwatch.ElapsedMilliseconds}ms for file {filePath} called via {file} line {line}");
            }
        }
    }

    public Task WriteFile(string filePath, Action<string> writeAction, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1)
    {
        logger.LogTrace("WriteFile Lock requested on file {filePath}", filePath);
        EnterLock(filePath, ConcurrentFileLockEnum.WriteLock);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            writeAction(filePath);
        }
        finally
        {
            ExitLock(filePath);
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > millisecondLoggingThreshold)
            {
                logger.LogDebug($"CFM.WriteFile() used {stopwatch.ElapsedMilliseconds}ms for file {filePath} called via {file} line {line}");
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteFile(string filePath, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1)
    {
        logger.LogTrace("DeleteFile Lock requested on file {filePath}", filePath);
        EnterLock(filePath, ConcurrentFileLockEnum.WriteLock);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            File.Delete(filePath);
        }
        finally
        {
            ExitLock(filePath);
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > millisecondLoggingThreshold)
            {
                logger.LogDebug($"CFM.DeleteFile() used {stopwatch.ElapsedMilliseconds}ms for file {filePath} called via {file} line {line}");
            }
        }

        return Task.CompletedTask;
    }

    public Task MoveFile(string sourcePath, string destinationPath, Action<string, string> moveAction, [CallerFilePath] string file = "", [CallerLineNumber] int line = -1)
    {
        logger.LogTrace("MoveFile Lock requested on source file {sourcePath}", sourcePath);
        // Lock destination first to avoid deadlocks
        EnterLock(destinationPath, ConcurrentFileLockEnum.WriteLock);

        try
        {
            logger.LogTrace("MoveFile Lock requested on destination file {destinationPath}", destinationPath);
            EnterLock(sourcePath, ConcurrentFileLockEnum.WriteLock);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                moveAction(sourcePath, destinationPath);
            }
            finally
            {
                ExitLock(sourcePath);
                stopwatch.Stop();
                if (stopwatch.ElapsedMilliseconds > millisecondLoggingThreshold)
                {
                    logger.LogDebug($"CFM.MoveFile() used {stopwatch.ElapsedMilliseconds}ms for file {sourcePath} {destinationPath} called via {file} line {line}");
                }
            }
        }
        finally
        {
            ExitLock(destinationPath);
        }

        return Task.CompletedTask;
    }

    private void LogLockStackTrace(string filePath, ConcurrentFileLockEnum lockType, int referenceCount)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            var methods = GetCallStack();
            var threadId = Thread.CurrentThread.ManagedThreadId;
            logger.LogTrace(
                "\n\nLock\n\tThreadId:{threadId} \n\tLockType:{lockType} \n\tFile path [{filePath}]\n\tReference Count: [{referenceCount}]\n\tStack:[{methods}]\n\n",
                threadId, lockType, filePath, referenceCount, methods);
        }
    }

    private void LogUnlockStackTrace(string filePath)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            var methods = GetCallStack();
            var threadId = Thread.CurrentThread.ManagedThreadId;
            logger.LogTrace(
                "\n\nUnlock\n\tThreadId:{threadId} \n\tFile path [{filePath}]\n\tStack:[{methods}]\n\n",
                threadId, filePath, methods);
        }
    }

    private string GetCallStack()
    {
        if (!logger.IsEnabled(LogLevel.Trace))
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