using System.Linq;
using System.Net.Http.Headers;

namespace Odin.Services.Util;

public static class HttpExtensions
{
    public static bool IsTrue(this HttpResponseHeaders headers, string headerName)
    {
        var value = headers.TryGetValues(headerName, out var values) &&
                    bool.TryParse(values.SingleOrDefault() ?? bool.FalseString, out var isIcrIssue) && isIcrIssue;

        return value;
    }
}