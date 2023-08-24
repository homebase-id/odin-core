using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using YouAuthClientReferenceImplementation.Models;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;

namespace YouAuthClientReferenceImplementation.Controllers;

[Route("ClientTypeApp")]
public class ClientTypeAppController : BaseController
{
    private const string IdentityCookieName = "OdinAppIdentity";
    private string LoggedInIdentity => Request.Cookies[IdentityCookieName] ?? "";

    private readonly ILogger<ClientTypeAppController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, State> _stateMap;

    public ClientTypeAppController(
        ILogger<ClientTypeAppController> logger,
        IHttpClientFactory httpClientFactory,
        ConcurrentDictionary<string, State> stateMap) : base(logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _stateMap = stateMap;
    }

    //

    // GET
    public IActionResult Index()
    {
        if (LoggedInIdentity == "")
        {
            return View(new ClientTypeAppIndexViewModel
            {
                LoggedInMessage = "Not logged in",
                ButtonCaption = "Log in"
            });
        }

        return View(new ClientTypeAppIndexViewModel
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

        var appParams = GetAppPhotosParams();

        var controllerRoute = ControllerContext.RouteData.Values["controller"]?.ToString() ?? "";
        var payload = new YouAuthAuthorizeRequest
        {
            ClientId = thirdParty,
            ClientInfo = "",
            ClientType = ClientType.app,
            PermissionRequest = OdinSystemSerializer.Serialize(appParams),
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

    [HttpGet("[controller]/authorization-code-callback")]
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
            TokenDeliveryOption = TokenDeliveryOption.json,
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

    //

    private YouAuthAppParameters GetAppPhotosParams()
    {
        var driveParams = new[]
        {
            new
            {
                a = "6483b7b1f71bd43eb6896c86148668cc",
                t = "2af68fe72fb84896f39f97c59d60813a",
                n = "Photo Library",
                d = "Place for your memories",
                p = 3
            },
        };
        var appParams = new YouAuthAppParameters
        {
            AppName = "Odin - Photos",
            AppOrigin = "dev.dotyou.cloud:3005",
            AppId = "32f0bdbf-017f-4fc0-8004-2d4631182d1e",
            ClientFriendly = "Firefox | macOS",
            DrivesParam = OdinSystemSerializer.Serialize(driveParams),
            Pk = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA21Hd52i8IyhMbhR9EXM0iRRI5bD7Su5MpK5WmczEEK6p%2FAAqLPPHJsreYpQHBOchd1cOTlwj4C257gRI3S2jTkI%2Fjny2u0ShzXiGr8%2BgwgmhWQYPua3QJyf4FnWFDvNO70Vw7jIe2PfSEw%2FoW718Yq1fR%2FiRasYLbzFuApwMYi%2BiD75tgIeDBnMMdgmo9JqoUq2XP5y4j4IVenVjLQqtFJezINiJQjUe2KatlofweVrYfhs3BDoJ8bdLSbGfy413QRd%2BhE4UTebi%2FQxSdAwO4Fy82%2FyKIi80qnK%2FF4qFE3q60cBTULI826cSryAulA7xOe%2B5qbyAOYh76OsICegotwIDAQAB",
            Return = "backend-will-decide",
        };

        return appParams;
    }


}