using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Odin.Hosting.Middleware;

namespace Odin.Hosting.Tests.Middleware;

#nullable enable

public class HttpsPortRedirectMiddlewareTest
{
    private static async Task<HttpContext> InvokeAsync(string queryString)
    {
        var middleware = new HttpsPortRedirectMiddleware(NullLogger<HttpsPortRedirectMiddleware>.Instance,
            _ => Task.CompletedTask, 443);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("frodo.example.com");
        context.Request.Path = "/index.php";
        context.Request.QueryString = new QueryString(queryString);

        await middleware.Invoke(context);
        return context;
    }

    [Test]
    public async Task ItShouldRedirectPlainHttpToHttps()
    {
        var context = await InvokeAsync("?a=b");

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status302Found));
        Assert.That(context.Response.Headers.Location.ToString(), Is.EqualTo("https://frodo.example.com/index.php?a=b"));
    }

    // #2080: Kestrel throws on a Location header carrying a control character, which ended as a 500 and an ERR
    [TestCase("?a=\u0007")]
    [TestCase("?a=\r")]
    [TestCase("?a=é")]
    public async Task ItShouldAnswer400ForAUrlALocationHeaderCantCarry(string queryString)
    {
        var context = await InvokeAsync(queryString);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status400BadRequest));
        Assert.That(context.Response.Headers.Location.ToString(), Is.Empty);
    }
}
