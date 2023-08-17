namespace ThirdPartyApp;

public static class Extensions
{
    public static Dictionary<string, string> GetCookies(this HttpResponseMessage response)
    {
        var result = new Dictionary<string, string>();

        if (response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
        {
            foreach (var cookieHeader in cookieHeaders)
            {
                var name = cookieHeader.Split(';')[0].Split('=')[0];
                var value = cookieHeader.Split(';')[0].Split('=')[1];
                result[name] = value;
            }
        }

        return result;
    }
}