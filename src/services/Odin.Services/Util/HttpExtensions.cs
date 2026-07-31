using System;
using System.Linq;
using System.Net.Http.Headers;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Refit;

namespace Odin.Services.Util;

public static class HttpExtensions
{
    public static bool IsTrue(this HttpResponseHeaders headers, string headerName)
    {
        var value = headers.TryGetValues(headerName, out var values) &&
                    bool.TryParse(values.SingleOrDefault() ?? bool.FalseString, out var isIcrIssue) && isIcrIssue;

        return value;
    }
    
    public static OdinClientErrorCode ParseProblemDetails(this ApiException apiException)
    {
        var pd = OdinSystemSerializer.Deserialize<ProblemDetails>(apiException.Content!);
        var codeText = pd.Extensions["errorCode"].ToString();
        var code = Enum.Parse<OdinClientErrorCode>(codeText!, true);
        return code;
    }

    /// <summary>
    /// Best-effort version of <see cref="ParseProblemDetails"/> for cases where the response might not be
    /// problem-details at all (a proxy error page, an older peer, an empty body).
    /// </summary>
    public static bool TryParseProblemDetails(this ApiException apiException, out OdinClientErrorCode code)
    {
        code = OdinClientErrorCode.NoErrorCode;

        if (string.IsNullOrWhiteSpace(apiException?.Content))
        {
            return false;
        }

        try
        {
            var pd = OdinSystemSerializer.Deserialize<ProblemDetails>(apiException.Content);
            if (pd?.Extensions == null || !pd.Extensions.TryGetValue("errorCode", out var raw))
            {
                return false;
            }

            return Enum.TryParse(raw?.ToString(), true, out code);
        }
        catch (Exception)
        {
            return false;
        }
    }
}