using System;
using System.IO;
using System.Threading;
using Serilog;

namespace Odin.Core.Services.Drives.DriveCore.Storage;

public static class IoUtils
{
    public static void RetryOperation(Action action, int maxAttempts, int delayInMilliseconds, string logMessage)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        int attempts = 0;
        do
        {
            try
            {
                Log.Information($"Attempt #{attempts} to perform [{logMessage}]");
                action();
                return; // Success, exit the loop.
            }
            catch
            {
                attempts++;
                if (attempts >= maxAttempts)
                {
                    Log.Warning($"Max attempts ({maxAttempts}) reached; delay between tries was {delayInMilliseconds}ms");
                    throw; // Rethrow the exception as all attempts have failed.
                }

                Thread.Sleep(delayInMilliseconds); // Wait before retrying.
            }
        } while (true);
    }

    public static bool WaitForFileUnlock(string filePath, TimeSpan timeout)
    {
        DateTime start = DateTime.Now;
        while (DateTime.Now - start < timeout)
        {
            try
            {
                // Try to open the file with read access and share mode set to none.
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                    return true; // File is unlocked and accessible.
                }
            }
            catch (IOException)
            {
                // File is still locked, wait for a short period before retrying.
                Thread.Sleep(100);
            }
        }

        return false; // Timeout expired, file is still locked.
    }
}