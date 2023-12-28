using System;
using System.Threading;

namespace Odin.Core.Services.Drives.DriveCore.Storage;

public static class Retry
{
    public static void RetryOperation(Action action, int maxAttempts, int delayInMilliseconds)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        int attempts = 0;
        do
        {
            try
            {
                action();
                return; // Success, exit the loop.
            }
            catch
            {
                attempts++;
                if (attempts >= maxAttempts)
                {
                    throw; // Rethrow the exception as all attempts have failed.
                }

                Thread.Sleep(delayInMilliseconds); // Wait before retrying.
            }
        } while (true);
    }
}