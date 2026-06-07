using System;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Odin.Hosting;

/// <summary>
/// Shared deep-link fallback for the statically-served single-page front-end apps
/// (owner, feed, chat, mail, community, chat-wasm). Each app registers its own
/// <c>UseStaticFiles</c> to serve real assets; whatever the static middleware does not
/// match falls through to this terminal handler.
///
/// The trap this guards against: a fallback that returns <c>index.html</c> for EVERY
/// unmatched request also masks a genuinely missing or misconfigured <em>asset</em> as a
/// <c>200 text/html</c>. Code that fetched the asset then receives HTML, tries to parse it
/// as the asset, and fails far from the real cause. This is not hypothetical — Compose
/// Multiplatform's web build fetches its string table
/// (<c>composeResources/.../strings.commonMain.cvr</c>) over HTTP; when that 404'd into this
/// fallback it received <c>index.html</c>, parsed HTML as the string table, and rendered
/// every label blank. It cost days to trace precisely because the server answered 200.
///
/// So the rule here is: only a browser <em>navigation</em> (which sends
/// <c>Accept: text/html</c>) gets the SPA shell; every other request — script, style, image,
/// <c>fetch()</c>/XHR, all of which omit <c>text/html</c> from <c>Accept</c> — gets a clean
/// <c>404</c> so a missing asset surfaces as a missing asset. Accept-based (rather than
/// "does the path have a file extension") so that path-routed deep-links containing dots —
/// e.g. <c>/owner/connections/frodo.dotyou.cloud</c> — still resolve to the shell.
/// </summary>
public static class SpaFallback
{
    /// <summary>
    /// True when this request is a browser navigation that should receive the SPA shell.
    /// Asset loads (fetch/XHR/&lt;script&gt;/&lt;link&gt;/&lt;img&gt;) send an <c>Accept</c>
    /// that does not include <c>text/html</c> and must instead 404.
    /// </summary>
    public static bool WantsHtmlDocument(HttpRequest request)
    {
        foreach (var accept in request.Headers.Accept)
        {
            if (!string.IsNullOrEmpty(accept) &&
                accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Terminal <c>Run</c> handler for a statically-served SPA: serve <paramref name="indexHtmlPath"/>
    /// for navigation requests, otherwise respond <c>404</c> so missing assets fail loudly instead of
    /// being masked as the SPA shell.
    /// </summary>
    public static async Task ServeShellOrNotFound(HttpContext context, string indexHtmlPath)
    {
        if (!WantsHtmlDocument(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.Headers.ContentType = MediaTypeNames.Text.Html;
        await context.Response.SendFileAsync(indexHtmlPath);
    }
}
