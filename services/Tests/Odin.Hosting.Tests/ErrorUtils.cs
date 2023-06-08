using System.Net;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Refit;

namespace Odin.Hosting.Tests;

public static class ErrorUtils
{
    public static ProblemDetails GetProblemDetails(ApiException error)
    {
        var details = OdinSystemSerializer.Deserialize<ProblemDetails>(error!.Content!);
        return details;
    }

    public static void AssetClientErrorCode(ApiException exception, YouverseClientErrorCode expectedCode)
    {
        Assert.IsTrue(MatchesClientErrorCode(exception, expectedCode));
    }

    public static bool MatchesClientErrorCode(ApiException exception, YouverseClientErrorCode expectedCode)
    {
        if (exception.StatusCode != HttpStatusCode.BadRequest)
        {
            return false;
        }
        
        var details = GetProblemDetails(exception);
        var code = (YouverseClientErrorCode)int.Parse(details.Extensions["errorCode"].ToString()!);
        return code == expectedCode;
    }
}