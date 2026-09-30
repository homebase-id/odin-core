using System.Net;

namespace Odin.Cli.Commands.Base;

internal static class ApiResponse
{
    /// <summary>
    /// Throws unless the admin API answered <paramref name="expected"/>, with the server's reason when it gave one: a
    /// bare "BadRequest" leaves the operator guessing why.
    /// </summary>
    public static async Task EnsureAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        var reason = (await response.Content.ReadAsStringAsync()).Trim();
        throw new Exception($"{response.RequestMessage?.RequestUri}: {response.StatusCode}" + (reason.Length > 0 ? $": {reason}" : ""));
    }
}
