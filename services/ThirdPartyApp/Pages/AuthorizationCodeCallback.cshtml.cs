using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Odin.Core.Serialization;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;

namespace ThirdPartyApp.Pages;

public class AuthorizationCodeCallback : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Code { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? State { get; set; }

    private readonly ILogger<IndexModel> _logger;
    private readonly ConcurrentDictionary<string, State> _stateMap;
    private readonly IHttpClientFactory _httpClientFactory;

    public AuthorizationCodeCallback(
        ILogger<IndexModel> logger,
        ConcurrentDictionary<string, State> stateMap,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _stateMap = stateMap;
    }

    public async Task<ActionResult> OnGet()
    {
        if (State == null || !_stateMap.TryGetValue(State, out var state))
        {
            return Redirect("/");
        }

        //
        // Exchange authorization code for token
        //

        var code = Code;
        var uri = new UriBuilder($"https://{state.Identity}{OwnerApiPathConstants.YouAuthV1Token}");
        var tokenRequest = new YouAuthTokenRequest
        {
            Code = code,
            CodeVerifier = state.CodeVerifier,
            TokenDeliveryOption = TokenDeliveryOption.cookie
        };
        var body = OdinSystemSerializer.Serialize(tokenRequest);

        var request = new HttpRequestMessage(HttpMethod.Post, uri.ToString())
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var client = _httpClientFactory.CreateClient("default");
        var response = await client.SendAsync(request);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            // SEB:TODO DO SOMETHING!
            return Redirect("/");
        }

        var json = await response.Content.ReadAsStringAsync();
        var _ = OdinSystemSerializer.Deserialize<YouAuthTokenResponse>(json);

        //
        // Store thirdparty cookies
        //

        // Save "sam was here" in cookie and this Sam is logged in with his ODIN identity! Hoorah!
        var cookies = response.GetCookies();
        var cat = cookies["XT32"];

        var cookieOption = new CookieOptions
        {
            Expires = DateTime.Now.AddDays(30)
        };
        Response.Cookies.Append("OdinIdentity", state.Identity, cookieOption);
        Response.Cookies.Append("OdinCat", cat, cookieOption);

        return Redirect("/");
    }
}