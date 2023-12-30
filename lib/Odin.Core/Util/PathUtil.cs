using System.IO;
using System.Runtime.InteropServices;

namespace Odin.Core.Util
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

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
    }


    public static class PathUtil
    {
        /// <summary>
        /// Switches the path to match the currently running OS
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string OsIfy(string path)
        {
            char target = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? '/' : '\\';
            return path.Replace(target, Path.DirectorySeparatorChar);
        }

        public static string Combine(params string[] paths)
        {
            return OsIfy(Path.Combine(paths));
        }
    }
}