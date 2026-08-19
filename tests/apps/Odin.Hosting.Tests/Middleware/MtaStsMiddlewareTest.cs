using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using Odin.Hosting.Middleware;
using Odin.Services.Configuration;

namespace Odin.Hosting.Tests.Middleware;

#nullable enable

// Plain unit tests - no WebScaffold. The middleware runs before tenant resolution and the
// apex-redirect middleware (RFC 8461 forbids redirects on the policy fetch), so its whole
// behavior is a function of (config, host, path).
public class MtaStsMiddlewareTest
{
    private static OdinConfiguration Config(bool enabled)
    {
        return new OdinConfiguration
        {
            Email = new OdinConfiguration.EmailSection
            {
                TenantMail = new OdinConfiguration.TenantMailSection
                {
                    Enabled = enabled,
                    MxNodes = ["node-a.id.pub", "node-b.id.pub"],
                }
            }
        };
    }

    private static async Task<(HttpContext context, bool nextCalled)> InvokeAsync(
        bool enabled, string host, string path, string method = "GET")
    {
        var nextCalled = false;
        var middleware = new MtaStsMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, Config(enabled));

        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();

        await middleware.Invoke(context);
        return (context, nextCalled);
    }

    private static string Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return new StreamReader(context.Response.Body).ReadToEnd();
    }

    [Test]
    public async Task ItShouldServeThePolicyOnTheMtaStsHost()
    {
        var (context, nextCalled) = await InvokeAsync(
            enabled: true, "mta-sts.frodo.example.com", "/.well-known/mta-sts.txt");

        Assert.That(nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
        Assert.That(context.Response.ContentType, Is.EqualTo("text/plain"));
        Assert.That(Body(context), Is.EqualTo(
            "version: STSv1\nmode: testing\nmx: node-a.id.pub\nmx: node-b.id.pub\nmax_age: 86400\n"));
    }

    [Test]
    public async Task ItShouldServeNothingElseOnTheMtaStsHost()
    {
        var (context, nextCalled) = await InvokeAsync(
            enabled: true, "mta-sts.frodo.example.com", "/some/other/path");

        Assert.That(nextCalled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task ItShouldPassThroughOtherHosts()
    {
        var (_, nextCalled) = await InvokeAsync(
            enabled: true, "frodo.example.com", "/.well-known/mta-sts.txt");

        Assert.That(nextCalled, Is.True);
    }

    [Test]
    public async Task ItShouldBeInertWhenTenantMailIsDisabled()
    {
        // Byte-identical behavior to the pre-email era: the request falls through to the
        // regular pipeline (where tenant resolution 404s the unknown prefix, as before)
        var (_, nextCalled) = await InvokeAsync(
            enabled: false, "mta-sts.frodo.example.com", "/.well-known/mta-sts.txt");

        Assert.That(nextCalled, Is.True);
    }
}
