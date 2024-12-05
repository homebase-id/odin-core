using System;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Util;

public static class FinalizerError
{
    public static void ReportMissingDispose(Type type, ILogger logger)
    {
        var errorMessage = $"Error {type.Name}: missing call to Dispose() or DisposeAsync()";

        try
        {
            logger?.LogError("{Message}", errorMessage);
        }
        catch (Exception)
        {
            // swallow so we don't mess up the finalizer
        }
        Console.Error.WriteLine(errorMessage);

#if DEBUG
        throw new InvalidOperationException(errorMessage);
#endif
    }
}