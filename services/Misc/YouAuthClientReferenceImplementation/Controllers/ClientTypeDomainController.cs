using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using YouAuthClientReferenceImplementation.Models;

namespace YouAuthClientReferenceImplementation.Controllers;

[Route("ClientTypeDomain")]
public class ClientTypeDomainController : BaseController
{
    private const string IdentityCookieName = "OdinDomainIdentity";
    private string LoggedInIdentity => Request.Cookies[IdentityCookieName] ?? "";

    private readonly ILogger<ClientTypeDomainController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, State> _stateMap;

    public ClientTypeDomainController(
        ILogger<ClientTypeDomainController> logger,
        IHttpClientFactory httpClientFactory,
        ConcurrentDictionary<string, State> stateMap) : base(logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _stateMap = stateMap;
    }

    //

    [HttpGet]
    public IActionResult Index()
    {
        if (LoggedInIdentity == "")
        {
            return View(new ClientTypeDomainIndexViewModel
            {
                LoggedInMessage = "Not logged in",
                ButtonCaption = "Log in"
            });
        }

        // SEB:TODO fix this when youauth token has been fixed
        // var uri = $"https://{LoggedInIdentity}/api/youauth/v1/auth/ping";
        // var request = new HttpRequestMessage(HttpMethod.Get, uri)
        // {
        //     Headers = { { "Cookie", new Cookie("XT32", OdinCat).ToString() } }
        // };
        // var client = _httpClientFactory.CreateClient("default");
        // var response = await client.SendAsync(request);

        return View(new ClientTypeDomainIndexViewModel
        {
            LoggedInMessage = $"Logged in as {LoggedInIdentity}",
            ButtonCaption = "Log out",
        });
    }

    //

    [HttpPost]
    public IActionResult Index(ClientTypeDomainIndexViewModel model)
    {
        return LoggedInIdentity == "" ? LogIn(model.LoggedInIdentity) : LogOut();
    }

    //

    private IActionResult LogIn(string identity)
    {
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, 1);

        const string thirdParty = "thirdparty.dotyou.cloud";

        var state = Guid.NewGuid().ToString();
        _stateMap[state] = new State
        {
            Identity = identity,
            PrivateKey = privateKey,
            KeyPair = keyPair
        };

        var controllerRoute = ControllerContext.RouteData.Values["controller"]?.ToString() ?? "";
        var payload = new YouAuthAuthorizeRequest
        {
            ClientId = thirdParty,
            ClientInfo = "",
            ClientType = ClientType.domain,
            PermissionRequest = "",
            PublicKey = keyPair.PublicKeyJwkBase64Url(),
            RedirectUri = $"https://{Request.Host}/{controllerRoute}/authorization-code-callback",
            State = state,
        };

        var uri =
            new UriBuilder($"https://{identity}{OwnerApiPathConstants.YouAuthV1Authorize}")
            {
                Query = payload.ToQueryString()
            }.ToString();

        return Redirect(uri);
    }

    //

    private IActionResult LogOut()
    {
        Response.Cookies.Delete(IdentityCookieName);
        return RedirectToAction("Index");
    }

    //

    [HttpGet("authorization-code-callback")]
    public async Task<IActionResult> AuthorizationCodeCallback(
        [FromQuery(Name = YouAuthDefaults.Code)] string code,
        [FromQuery(Name = YouAuthDefaults.State)] string stateKey,
        [FromQuery(Name = YouAuthDefaults.PublicKey)] string publicKey,
        [FromQuery(Name = YouAuthDefaults.Salt)] string salt)
    {
        if (!_stateMap.TryGetValue(stateKey, out var state))
        {
            return RedirectToAction("Index");
        }

        //
        // Exchange authorization code for token
        //

        var privateKey = state.PrivateKey!;
        var keyPair = state.KeyPair!;
        var remotePublicKey = publicKey;
        var remoteSalt = Convert.FromBase64String(salt);

        var remotePublicKeyDer = EccPublicKeyData.FromJwkBase64UrlPublicKey(remotePublicKey);
        var exchangeSecret = keyPair.GetEcdhSharedSecret(privateKey, remotePublicKeyDer, remoteSalt);
        var exchangeSecretDigest = SHA256.Create().ComputeHash(exchangeSecret.GetKey()).ToBase64();

        var uri = new UriBuilder($"https://{state.Identity}{OwnerApiPathConstants.YouAuthV1Token}");
        var tokenRequest = new YouAuthTokenRequest
        {
            Code = code,
            TokenDeliveryOption = TokenDeliveryOption.cookie,
            SecretDigest = exchangeSecretDigest
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

        // Save "sam was here" in cookie and then Sam is logged in with his ODIN identity! Hoorah!
        var cookies = response.GetCookies();
        var cat = cookies["XT32"];

        var cookieOption = new CookieOptions
        {
            Expires = DateTime.Now.AddDays(30)
        };
        Response.Cookies.Append(IdentityCookieName, state.Identity, cookieOption);

        return RedirectToAction("Index");
    }


}
