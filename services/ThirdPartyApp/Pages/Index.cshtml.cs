using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Odin.Core;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;

namespace ThirdPartyApp.Pages;

public class IndexModel : PageModel
{
    [BindProperty]
    public string FormOdinIdentity { get; set; } = "";

    public string LoggedInMessage { get; set; } = "Not logged in";
    public string ButtonCaption { get; set; } = "Log in";

    private readonly ILogger<IndexModel> _logger;
    private readonly ConcurrentDictionary<string, State> _stateMap;

    public string LoggedInIdentity => Request.Cookies["OdinIdentity"] ?? "";
    public string OdinCat => Request.Cookies["OdinCat"] ?? "";

    private readonly IHttpClientFactory _httpClientFactory;

    public IndexModel(
        ILogger<IndexModel> logger,
        ConcurrentDictionary<string, State> stateMap,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _stateMap = stateMap;
        _httpClientFactory = httpClientFactory;
    }

    //

    public async Task OnGet()
    {
        if (LoggedInIdentity == "")
        {
            LoggedInMessage = "Not logged in";
            ButtonCaption = "Log in";
            return;
        }

        LoggedInMessage = $"Logged in as {LoggedInIdentity}";
        ButtonCaption = "Log out";

        // SEB:TODO fix this when youauth token has been fixed
        // var uri = $"https://{LoggedInIdentity}/api/youauth/v1/auth/ping";
        // var request = new HttpRequestMessage(HttpMethod.Get, uri)
        // {
        //     Headers = { { "Cookie", new Cookie("XT32", OdinCat).ToString() } }
        // };
        // var client = _httpClientFactory.CreateClient("default");
        // var response = await client.SendAsync(request);
    }

    //

    public IActionResult OnPost()
    {
        return LoggedInIdentity == "" ? LogIn() : LogOut();
    }

    //

    private IActionResult LogIn()
    {
        var codeVerifier = Guid.NewGuid().ToString();
        var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();
        const string thirdParty = "thirdparty.dotyou.cloud";

        var state = Guid.NewGuid().ToString();
        _stateMap[state] = new State
        {
            CodeChallenge = codeChallenge,
            CodeVerifier = codeVerifier,
            Identity = FormOdinIdentity
        };

        var payload = new YouAuthAuthorizeRequest
        {
            ClientId = thirdParty,
            ClientType = ClientType.domain,
            ClientInfo = "",
            CodeChallenge = codeChallenge,
            PermissionRequest = "",
            State = state,
            RedirectUri = $"https://{Request.Host}/authorization-code-callback"
        };

        var uri =
            new UriBuilder($"https://{FormOdinIdentity}{OwnerApiPathConstants.YouAuthV1Authorize}")
            {
                Query = payload.ToQueryString()
            }.ToString();

        return Redirect(uri);
    }

    //

    private IActionResult LogOut()
    {
        Response.Cookies.Delete("OdinIdentity");
        return Redirect("/");
    }
}
