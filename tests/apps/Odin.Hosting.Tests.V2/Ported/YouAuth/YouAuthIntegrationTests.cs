#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Hosting.Controllers.Home.Auth;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.Auth;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.Tests.YouAuthApi;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.YouAuth;

//
// Flow being tested:
// https://github.com/YouFoundation/stories-and-architecture-docs/blob/master/concepts/YouAuth/unified-authorization.md
//

/// <summary>
/// Port of <c>YouAuthApi/IntegrationTests/YouAuthIntegrationTests</c> (and of its one-subclass base
/// class, <c>YouAuthIntegrationTestBase</c>, whose helpers are inlined here). The unified-authorization
/// flow driven over raw HTTP: the authorize endpoint's redirects to login / consent / app-registration,
/// the consent POST, auth-code issuance, and the ECDH token exchange that ends in a usable client
/// access token.
/// </summary>
/// <remarks>
/// <b>Why the URIs still carry <c>:8443</c>.</b> Almost every assertion here reads a URL the server
/// built out of <c>Request.Host</c> (<c>YouAuthUnifiedController</c> and
/// <c>OwnerAuthenticationHandler</c> both interpolate it straight into their redirect targets), so the
/// port literal in the request is what the port literal in the expectation is. Nothing listens on
/// 8443 in this framework — the requests are absolute URIs sent through the in-process TestServer
/// handler, which routes on the Host header's host part only (<c>MultiTenantContainerMiddleware</c>
/// reads <c>Request.Host.Host</c>) — but keeping it means every request body, URL and assertion moves
/// across byte for byte. <see cref="HttpsPort"/> replaces <c>WebScaffold.HttpsPort</c>, which holds
/// the same value; the constant is local so this fixture pulls in no scaffold.
/// <para>
/// <b>Fixture shape.</b> The original's base class put <c>RunBeforeAnyTests</c> in <c>[SetUp]</c>
/// rather than <c>[OneTimeSetUp]</c>, so it booted a whole <c>WebScaffold</c> — four identities each —
/// twelve times, once per test. Here one host serves the fixture and each test starts from a restored
/// snapshot.
/// </para>
/// <para>
/// <b>What moved and what didn't.</b> Six tests opened with
/// <c>DisconnectHobbits(Frodo, Samwise)</c>, which existed because <c>WebScaffold</c> state leaked
/// between tests; the per-test snapshot restore leaves the two disconnected already, so those calls
/// are dropped as lifecycle. <c>ConnectHobbits</c> becomes <see cref="PeerFlow.ConnectAllAsync"/> over
/// the same four identities — it does not register the per-hobbit app <c>CreateConnectedHobbits</c>
/// did, and no assertion here reads one. The base class's <c>GetAppPhotosParams</c> had no caller and
/// is not carried.
/// </para>
/// <para>
/// <b>Owner login.</b> <see cref="AuthenticateOwnerReturnOwnerCookieAndSharedSecret"/> is carried
/// verbatim rather than replaced by <c>LoginAsOwner</c>: it is four assertions about the owner-auth
/// endpoints (verify-token false, nonce, authenticate, verify-token true) as much as it is arrange.
/// It relies on the password already being set, which <c>V2Fixture</c>'s warm-up does with the same
/// literal (<c>OwnerLogin.DefaultPassword</c> and <c>YouAuthTestHelper.Password</c> are both
/// <c>EnSøienØ</c>).
/// </para>
/// <para>
/// <b>Carried defect.</b> <c>b3_domain_ConsentNeededWhenPreviousConsentHasExpired</c> sleeps two real
/// seconds waiting for a one-second consent to expire. That is a wall-clock wait, not a poll of a
/// background service, so there is nothing to convert it to; it stays, and it is the slowest test in
/// the fixture.
/// </para>
/// <para>
/// No <c>SetupCallerWithOwner</c> is used (every caller here is hand-built raw HTTP), so its
/// create-drive/build-caller ordering caveat does not apply.
/// </para>
/// </remarks>
[TestFixture]
public class YouAuthIntegrationTests : V2Fixture
{
    /// <summary>
    /// The port the original's URLs carried. See the class remarks: nothing binds it here, but the
    /// server echoes the request's Host — port included — into the redirects these tests assert on.
    /// </summary>
    private const string HttpsPort = "8443";

    protected override string[] HostIdentities =>
        [Identities.Frodo, Identities.Sam, Identities.Pippin, Identities.Merry];

    [Test]
    public async Task a1_AuthorizeEndpointMustRedirectToLogonIfUserNotAuthenticated()
    {
        const string hobbit = "sam.dotyou.cloud";
        var apiClient = Host.CreateClient();
        const string thirdParty = "frodo.dotyou.cloud";

        //
        // [010] Generate key pair
        //
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

        //
        // [030] Request authorization code
        //
        {
            //
            // Arrange
            //

            var payload = new YouAuthAuthorizeRequest
            {
                ClientId = Guid.NewGuid().ToString(),
                ClientType = ClientType.app,
                ClientInfo = "My Awesome App",
                PermissionRequest = "identity:read identity:write",
                PublicKey = keyPair.PublicKeyJwkBase64Url(),
                State = "somestate",
                RedirectUri = $"https://{thirdParty}:{HttpsPort}/authorization/code/callback"
            };

            var uri =
                new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                {
                    Query = payload.ToQueryString()
                }.Uri;

            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                // no owner cookie!
            };

            //
            // Act
            //
            var response = await apiClient.SendAsync(request);

            //
            // Assert
            //
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

            // ... /owner/login/youauth
            var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
            var loginUri = new Uri(location);
            Assert.That(loginUri.Scheme, Is.EqualTo("https"));
            Assert.That(loginUri.Host, Is.EqualTo($"{hobbit}"));
            Assert.That(loginUri.AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Login));

            // ... ?returnUrl= ...
            var qs = YouAuthTestHelper.ParseQueryString(location);
            var returnUrl = new Uri(qs["returnUrl"]);
            Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}"));

            // ... returnUrl components:
            var returnUrlComponents = YouAuthAuthorizeRequest.FromQueryString(returnUrl.Query);
            Assert.That(returnUrlComponents.ClientId, Is.EqualTo(payload.ClientId));
            Assert.That(returnUrlComponents.ClientType, Is.EqualTo(payload.ClientType));
            Assert.That(returnUrlComponents.PermissionRequest, Is.EqualTo(payload.PermissionRequest));
            Assert.That(returnUrlComponents.RedirectUri, Is.EqualTo(payload.RedirectUri));
            Assert.That(returnUrlComponents.State, Is.EqualTo(payload.State));
            Assert.That(returnUrlComponents.ClientInfo, Is.EqualTo(payload.ClientInfo));
            Assert.That(returnUrlComponents.PublicKey, Is.EqualTo(payload.PublicKey));
        }
    }

    //

    [Test]
    public async Task b0_AuthorizeEndpointMust400IfYouAuthingToSelf()
    {
        const string hobbit = "frodo.dotyou.cloud";
        var apiClient = Host.CreateClient();
        var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

        //
        // [010] Generate key pair
        //
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

        //
        // [030] Request authorization code
        //
        {
            //
            // Arrange
            //

            const string thirdParty = "frodo.dotyou.cloud";
            var payload = new YouAuthAuthorizeRequest
            {
                ClientId = thirdParty,
                ClientType = ClientType.domain,
                ClientInfo = "",
                PermissionRequest = "",
                PublicKey = keyPair.PublicKeyJwkBase64Url(),
                State = "somestate",
                RedirectUri = $"https://{thirdParty}:{HttpsPort}/authorization/code/callback"
            };

            var uri =
                new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                {
                    Query = payload.ToQueryString()
                }.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
            };

            //
            // Act
            //
            var response = await apiClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            var problemDetails = OdinSystemSerializer.Deserialize<ProblemDetails>(content);

            //
            // Assert
            //
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(problemDetails!.Title, Does.Contain("Cannot YouAuth to self"));
        }
    }

    //

    [Test]
    public async Task b1_domain_AuthorizeEndpointMust400IfMissingClientId()
    {
        string hobbit = Identities.Sam;
        var apiClient = Host.CreateClient();
        var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

        //
        // [010] Generate key pair
        //
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

        //
        // [030] Request authorization code
        //
        {
            //
            // Arrange
            //

            const string thirdParty = "frodo.dotyou.cloud";
            var payload = new YouAuthAuthorizeRequest
            {
                ClientId = "",
                ClientType = ClientType.domain,
                ClientInfo = "",
                PermissionRequest = "",
                PublicKey = keyPair.PublicKeyJwkBase64Url(),
                State = "somestate",
                RedirectUri = $"https://{thirdParty}:{HttpsPort}/authorization/code/callback"
            };

            var uri =
                new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                {
                    Query = payload.ToQueryString()
                }.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
            };

            //
            // Act
            //
            var response = await apiClient.SendAsync(request);

            //
            // Assert
            //
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }
    }

    //

    [Test]
    public async Task b2_domain_AuthorizeEndpointMustRedirectToConsentPageIfConsentIsNeeded()
    {
        const string hobbit = "sam.dotyou.cloud";
        var apiClient = Host.CreateClient();
        var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

        //
        // [010] Generate key pair
        //
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

        //
        // [030] Request authorization code
        //
        {
            //
            // Arrange
            //

            const string thirdParty = "frodo.dotyou.cloud";
            var payload = new YouAuthAuthorizeRequest
            {
                ClientId = thirdParty,
                ClientType = ClientType.domain,
                ClientInfo = "",
                PermissionRequest = "",
                PublicKey = keyPair.PublicKeyJwkBase64Url(),
                State = "somestate",
                RedirectUri = $"https://{thirdParty}:{HttpsPort}/authorization/code/callback"
            };

            var uri =
                new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                {
                    Query = payload.ToQueryString()
                }.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
            };

            //
            // Act
            //
            var response = await apiClient.SendAsync(request);

            //
            // Assert
            //
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

            // ... /owner/login/consent
            var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
            var consentUri = new Uri(location);
            Assert.That(consentUri.Scheme, Is.EqualTo("https"));
            Assert.That(consentUri.Host, Is.EqualTo($"{hobbit}"));
            Assert.That(consentUri.AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Consent));

            // ... ?returnUrl= ...
            var qs = YouAuthTestHelper.ParseQueryString(location);
            var returnUrl = new Uri(qs["returnUrl"]);
            Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}"));

            // ... returnUrl components:
            var returnUrlComponents = YouAuthAuthorizeRequest.FromQueryString(returnUrl.Query);
            Assert.That(returnUrlComponents.ClientId, Is.EqualTo(payload.ClientId));
            Assert.That(returnUrlComponents.ClientType, Is.EqualTo(payload.ClientType));
            Assert.That(returnUrlComponents.PermissionRequest, Is.EqualTo(payload.PermissionRequest));
            Assert.That(returnUrlComponents.RedirectUri, Is.EqualTo(payload.RedirectUri));
            Assert.That(returnUrlComponents.State, Is.EqualTo(payload.State));
            Assert.That(returnUrlComponents.ClientInfo, Is.EqualTo(payload.ClientInfo));
            Assert.That(returnUrlComponents.PublicKey, Is.EqualTo(payload.PublicKey));
        }
    }

    //

    [Test]
    public async Task b4_domain_ConsentNotRequiredWhenPreviousConsentDidNotExpire()
    {
        const string hobbit = "sam.dotyou.cloud";
        var apiClient = Host.CreateClient();
        var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

        const string thirdParty = "frodo.dotyou.cloud";
        var finalRedirectUri = new Uri($"https://{thirdParty}:{HttpsPort}/authorization/code/callback");

        //
        // FIRST RUN - get consent
        //
        {
            //
            // [010] Generate key pair
            //
            var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

            Uri returnUrl;

            //
            // [030] Request authorization code
            //
            var payload = new YouAuthAuthorizeRequest
            {
                ClientId = thirdParty,
                ClientType = ClientType.domain,
                ClientInfo = "",
                PermissionRequest = "",
                PublicKey = keyPair.PublicKeyJwkBase64Url(),
                State = "somestate",
                RedirectUri = $"https://{thirdParty}:{HttpsPort}/authorization/code/callback"
            };
            {
                var uri =
                    new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                    {
                        Query = payload.ToQueryString()
                    }.ToString();

                var request = new HttpRequestMessage(HttpMethod.Get, uri)
                {
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                };

                var response = await apiClient.SendAsync(request);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

                // ... /owner/youauth/consent
                var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                var consentUri = new Uri(location);
                Assert.That(consentUri.Scheme, Is.EqualTo("https"));
                Assert.That(consentUri.Host, Is.EqualTo($"{hobbit}"));
                Assert.That(consentUri.AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Consent));

                // ... ?returnUrl= ...
                var qs = YouAuthTestHelper.ParseQueryString(location);
                returnUrl = new Uri(qs["returnUrl"]);
                Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}"));
            }

            //
            // [050] Consent needed
            //
            {
                //
                // The frontend consent page (/owner/youauth/consent) shows a form
                // with app name, app description and scopes the user must authorize.
                //
                // When the user consents, i.e. clicks OK, the form does a POST to the backend authorize endpoint:
                //

                var uri = new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}");

                var consentRequirements = OdinSystemSerializer.Serialize(
                    new ConsentRequirements
                    {
                        ConsentRequirementType = ConsentRequirementType.Expiring,
                        Expiration = UnixTimeUtc.Now().AddDays(30)
                    });

                var request = new HttpRequestMessage(HttpMethod.Post, uri.ToString())
                {
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { YouAuthAuthorizeConsentGiven.ReturnUrlName, returnUrl.ToString() },
                        { YouAuthAuthorizeConsentGiven.ConsentRequirementName, consentRequirements},
                    })
                };

                var response = await apiClient.SendAsync(request);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

                var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                var authorizeUri = new Uri(location);
                Assert.That(authorizeUri.Scheme, Is.EqualTo("https"));
                Assert.That(authorizeUri.Host, Is.EqualTo($"{hobbit}"));
                Assert.That(authorizeUri.AbsolutePath, Is.EqualTo(OwnerApiPathConstants.YouAuthV1Authorize));
            }
        }

        //
        // SECOND RUN - consent from first run is still valid
        //
        {
            //
            // [010] Generate key pair
            //
            var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

            //
            // [030] Request authorization code, consent not needed
            //
            var payload = new YouAuthAuthorizeRequest
            {
                ClientId = thirdParty,
                ClientType = ClientType.domain,
                ClientInfo = "",
                PermissionRequest = "",
                PublicKey = keyPair.PublicKeyJwkBase64Url(),
                State = "somestate",
                RedirectUri = $"https://{thirdParty}:{HttpsPort}/authorization/code/callback"
            };
            {
                var uri =
                    new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                    {
                        Query = payload.ToQueryString()
                    }.ToString();

                var request = new HttpRequestMessage(HttpMethod.Get, uri)
                {
                    Headers =
                    {
                        { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() }
                    },
                };

                var response = await apiClient.SendAsync(request);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

                var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                var redirectUri = new Uri(location);
                Assert.That(redirectUri.Scheme, Is.EqualTo("https"));
                Assert.That(redirectUri.Host, Is.EqualTo($"{thirdParty}"));
                Assert.That(redirectUri.AbsolutePath, Is.EqualTo(finalRedirectUri.AbsolutePath));

                var qs = HttpUtility.ParseQueryString(redirectUri.Query);
                Console.WriteLine("qs = " + string.Join("; ",
                    qs.AllKeys.Select(key => $"{key}={string.Join(",", qs.GetValues(key) ?? Array.Empty<string>())}")));

                var identity = qs[YouAuthDefaults.Identity]!;
                var state = qs[YouAuthDefaults.State]!;
                var remotePublicKey = qs[YouAuthDefaults.PublicKey]!;
                var remoteSalt = qs[YouAuthDefaults.Salt]!;

                Assert.That(identity, Is.EqualTo(hobbit));
                Assert.That(state, Is.EqualTo(payload.State));
                Assert.That(remotePublicKey, Is.Not.Null.And.Not.Empty);
                Assert.That(remoteSalt, Is.Not.Null.And.Not.Empty);
            }
        }
    }

    //

    [Test]
    public async Task b3_domain_ConsentNeededWhenPreviousConsentHasExpired()
    {
        const string hobbit = "sam.dotyou.cloud";
        var apiClient = Host.CreateClient();
        var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

        const string thirdParty = "frodo.dotyou.cloud";
        var finalRedirectUri = new Uri($"https://{thirdParty}:{HttpsPort}/authorization/code/callback");

        //
        // FIRST RUN - get consent
        //
        {
            //
            // [010] Generate key pair
            //
            var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

            Uri returnUrl;

            //
            // [030] Request authorization code
            //
            var payload = new YouAuthAuthorizeRequest
            {
                ClientId = thirdParty,
                ClientType = ClientType.domain,
                ClientInfo = "",
                PermissionRequest = "",
                PublicKey = keyPair.PublicKeyJwkBase64Url(),
                State = "somestate",
                RedirectUri = $"https://{thirdParty}:{HttpsPort}/authorization/code/callback"
            };
            {
                var uri =
                    new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                    {
                        Query = payload.ToQueryString()
                    }.ToString();

                var request = new HttpRequestMessage(HttpMethod.Get, uri)
                {
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                };

                var response = await apiClient.SendAsync(request);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

                // ... /owner/youauth/consent
                var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                var consentUri = new Uri(location);
                Assert.That(consentUri.Scheme, Is.EqualTo("https"));
                Assert.That(consentUri.Host, Is.EqualTo($"{hobbit}"));
                Assert.That(consentUri.AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Consent));

                // ... ?returnUrl= ...
                var qs = YouAuthTestHelper.ParseQueryString(location);
                returnUrl = new Uri(qs["returnUrl"]);
                Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}"));
            }

            //
            // [050] Consent needed
            //
            Uri authorizeUri;
            {
                //
                // The frontend consent page (/owner/youauth/consent) shows a form
                // with app name, app description and scopes the user must authorize.
                //
                // When the user consents, i.e. clicks OK, the form does a POST to the backend authorize endpoint:
                //

                var uri = new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}");

                var consentRequirements = OdinSystemSerializer.Serialize(
                    new ConsentRequirements
                    {
                        ConsentRequirementType = ConsentRequirementType.Expiring,
                        Expiration = UnixTimeUtc.Now().AddSeconds(1)
                    });

                var request = new HttpRequestMessage(HttpMethod.Post, uri.ToString())
                {
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { YouAuthAuthorizeConsentGiven.ReturnUrlName, returnUrl.ToString() },
                        { YouAuthAuthorizeConsentGiven.ConsentRequirementName, consentRequirements},
                    })
                };

                var response = await apiClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), content);

                var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                authorizeUri = new Uri(location);
                Assert.That(authorizeUri.Scheme, Is.EqualTo("https"));
                Assert.That(authorizeUri.Host, Is.EqualTo($"{hobbit}"));
                Assert.That(authorizeUri.AbsolutePath, Is.EqualTo(OwnerApiPathConstants.YouAuthV1Authorize));
            }

            //
            // [070] Create auth code
            //
            {
                var request = new HttpRequestMessage(HttpMethod.Get, authorizeUri.ToString())
                {
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                };

                var response = await apiClient.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

                var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                var redirectUri = new Uri(location);
                Assert.That(redirectUri.Scheme, Is.EqualTo("https"));
                Assert.That(redirectUri.Host, Is.EqualTo($"{thirdParty}"));
                Assert.That(redirectUri.AbsolutePath, Is.EqualTo(finalRedirectUri.AbsolutePath));

                var qs = HttpUtility.ParseQueryString(redirectUri.Query);
                Console.WriteLine("qs = " + string.Join("; ",
                    qs.AllKeys.Select(key => $"{key}={string.Join(",", qs.GetValues(key) ?? Array.Empty<string>())}")));

                var identity = qs[YouAuthDefaults.Identity]!;
                var state = qs[YouAuthDefaults.State]!;
                var remotePublicKey = qs[YouAuthDefaults.PublicKey]!;
                var remoteSalt = qs[YouAuthDefaults.Salt]!;

                Assert.That(identity, Is.EqualTo(hobbit));
                Assert.That(state, Is.EqualTo(payload.State));
                Assert.That(remotePublicKey, Is.Not.Null.And.Not.Empty);
                Assert.That(remoteSalt, Is.Not.Null.And.Not.Empty);
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(2));

        //
        // SECOND RUN - consent from first run is no longer valid
        //
        {
            //
            // [010] Generate key pair
            //
            var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

            Uri returnUrl;

            //
            // [030] Request authorization code
            //
            var payload = new YouAuthAuthorizeRequest
            {
                ClientId = thirdParty,
                ClientType = ClientType.domain,
                ClientInfo = "",
                PermissionRequest = "",
                PublicKey = keyPair.PublicKeyJwkBase64Url(),
                State = "somestate",
                RedirectUri = $"https://{thirdParty}:{HttpsPort}/authorization/code/callback"
            };
            {
                var uri =
                    new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                    {
                        Query = payload.ToQueryString()
                    }.ToString();

                var request = new HttpRequestMessage(HttpMethod.Get, uri)
                {
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                };

                var response = await apiClient.SendAsync(request);

                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

                // ... /owner/youauth/consent
                var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                var consentUri = new Uri(location);
                Assert.That(consentUri.Scheme, Is.EqualTo("https"));
                Assert.That(consentUri.Host, Is.EqualTo($"{hobbit}"));
                Assert.That(consentUri.AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Consent));

                // ... ?returnUrl= ...
                var qs = YouAuthTestHelper.ParseQueryString(location);
                returnUrl = new Uri(qs["returnUrl"]);
                Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}"));
            }
        }
    }

    //

    [Test]
    public async Task b5_domain_WithExplicitConsentClientAccessTokenShouldBeDeliveredAsJsonResponse()
    {
        const string hobbit = "sam.dotyou.cloud";
        var apiClient = Host.CreateClient();
        var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

        //
        // [010] Generate key pair
        //
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

        Uri returnUrl;
        const string thirdParty = "frodo.dotyou.cloud";
        var finalRedirectUri = new Uri($"https://{thirdParty}:{HttpsPort}/authorization/code/callback");

        //
        // [030] Request authorization code
        //
        var payload = new YouAuthAuthorizeRequest
        {
            ClientId = thirdParty,
            ClientType = ClientType.domain,
            ClientInfo = "",
            PermissionRequest = "",
            PublicKey = keyPair.PublicKeyJwkBase64Url(),
            State = "somestate",
            RedirectUri = $"https://{thirdParty}:{HttpsPort}/authorization/code/callback"
        };
        {
            var uri =
                new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                {
                    Query = payload.ToQueryString()
                }.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
            };

            var response = await apiClient.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

            // ... /owner/youauth/consent
            var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
            var consentUri = new Uri(location);
            Assert.That(consentUri.Scheme, Is.EqualTo("https"));
            Assert.That(consentUri.Host, Is.EqualTo($"{hobbit}"));
            Assert.That(consentUri.AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Consent));

            // ... ?returnUrl= ...
            var qs = YouAuthTestHelper.ParseQueryString(location);
            returnUrl = new Uri(qs["returnUrl"]);
            Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}"));
        }

        //
        // [050] Consent needed
        //
        Uri authorizeUri;
        {
            //
            // The frontend consent page (/owner/youauth/consent) shows a form
            // with app name, app description and scopes the user must authorize.
            //
            // When the user consents, i.e. clicks OK, the form does a POST to the backend authorize endpoint:
            //

            var uri = new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}");

            var consentRequirements = OdinSystemSerializer.Serialize(
                new ConsentRequirements
                {
                    ConsentRequirementType = ConsentRequirementType.Expiring,
                    Expiration = UnixTimeUtc.Now().AddDays(30)
                });

            var request = new HttpRequestMessage(HttpMethod.Post, uri.ToString())
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { YouAuthAuthorizeConsentGiven.ReturnUrlName, returnUrl.ToString() },
                    { YouAuthAuthorizeConsentGiven.ConsentRequirementName, consentRequirements},
                })
            };

            var response = await apiClient.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

            var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
            authorizeUri = new Uri(location);
            Assert.That(authorizeUri.Scheme, Is.EqualTo("https"));
            Assert.That(authorizeUri.Host, Is.EqualTo($"{hobbit}"));
            Assert.That(authorizeUri.AbsolutePath, Is.EqualTo(OwnerApiPathConstants.YouAuthV1Authorize));
        }

        //
        // [070] Create auth code
        //
        string remotePublicKey, remoteSalt;
        {
            var request = new HttpRequestMessage(HttpMethod.Get, authorizeUri.ToString())
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
            };

            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

            var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
            var redirectUri = new Uri(location);
            Assert.That(redirectUri.Scheme, Is.EqualTo("https"));
            Assert.That(redirectUri.Host, Is.EqualTo($"{thirdParty}"));
            Assert.That(redirectUri.AbsolutePath, Is.EqualTo(finalRedirectUri.AbsolutePath));

            var qs = HttpUtility.ParseQueryString(redirectUri.Query);
            Console.WriteLine("qs = " + string.Join("; ",
                qs.AllKeys.Select(key => $"{key}={string.Join(",", qs.GetValues(key) ?? Array.Empty<string>())}")));

            var identity = qs[YouAuthDefaults.Identity]!;
            var state = qs[YouAuthDefaults.State]!;
            remotePublicKey = qs[YouAuthDefaults.PublicKey]!;
            remoteSalt = qs[YouAuthDefaults.Salt]!;

            Assert.That(identity, Is.EqualTo(hobbit));
            Assert.That(state, Is.EqualTo(payload.State));
            Assert.That(remotePublicKey, Is.Not.Null.And.Not.Empty);
            Assert.That(remoteSalt, Is.Not.Null.And.Not.Empty);
        }

        //
        // [90] Calculate shared secret and digtest for token exchange
        // [100] Exchange auth code for access token
        // [140] Return client access token to client
        //
        byte[] sharedSecret, clientAuthToken;
        {
            var remotePublicKeyJwk = EccPublicKeyData.FromJwkBase64UrlPublicKey(remotePublicKey);
            var exchangeSecret = keyPair.GetEcdhSharedSecret(privateKey, remotePublicKeyJwk, Convert.FromBase64String(remoteSalt));
            var exchangeSecretDigest = SHA256.Create().ComputeHash(exchangeSecret.GetKey()).ToBase64();

            var uri = new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Token}");
            var tokenRequest = new YouAuthTokenRequest
            {
                SecretDigest = exchangeSecretDigest
            };
            var body = OdinSystemSerializer.Serialize(tokenRequest);

            var request = new HttpRequestMessage(HttpMethod.Post, uri.ToString())
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var content = await response.Content.ReadAsStringAsync();
            var token = OdinSystemSerializer.Deserialize<YouAuthTokenResponse>(content);

            Assert.That(token!.Base64SharedSecretCipher, Is.Not.Null.And.Not.Empty);
            Assert.That(token!.Base64SharedSecretIv, Is.Not.Null.And.Not.Empty);
            Assert.That(token.Base64ClientAuthTokenCipher, Is.Not.Null.And.Not.Empty);
            Assert.That(token.Base64ClientAuthTokenIv, Is.Not.Null.And.Not.Empty);

            var sharedSecretCipher = Convert.FromBase64String(token.Base64SharedSecretCipher!);
            var sharedSecretIv = Convert.FromBase64String(token.Base64SharedSecretIv!);
            sharedSecret = AesCbc.Decrypt(sharedSecretCipher, exchangeSecret, sharedSecretIv);
            Assert.That(sharedSecret, Is.Not.Null.And.Not.Empty);

            var clientAuthTokenCipher = Convert.FromBase64String(token.Base64ClientAuthTokenCipher!);
            var clientAuthTokenIv = Convert.FromBase64String(token.Base64ClientAuthTokenIv!);
            clientAuthToken = AesCbc.Decrypt(clientAuthTokenCipher, exchangeSecret, clientAuthTokenIv);
            Assert.That(clientAuthToken, Is.Not.Null.And.Not.Empty);
        }

        // Access resource using cat and shared secret
        {
            var catBase64 = Convert.ToBase64String(clientAuthToken);
            var uri = YouAuthTestHelper.UriWithEncryptedQueryString($"https://{hobbit}:{HttpsPort}{HomeApiPathConstants.AuthV1}/{HomeApiPathConstants.PingMethodName}?text=helloworld", sharedSecret);
            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.HomeCookieName, catBase64).ToString() } }
            };
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var text = await YouAuthTestHelper.DecryptContent<string>(response, sharedSecret);
            Assert.That(text, Is.EqualTo($"ping from {hobbit}: helloworld"));
        }

    }

    //

    [Test]
    public async Task b6_domain_WithImplicitConsentClientAccessTokenShouldBeDeliveredAsJsonResponse()
    {
        const string hobbit = "sam.dotyou.cloud";
        var apiClient = Host.CreateClient();
        var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

        await ConnectHobbits();

        //
        // [010] Generate key pair
        //
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

        const string thirdParty = "frodo.dotyou.cloud";
        var finalRedirectUri = new Uri($"https://{thirdParty}:{HttpsPort}/authorization/code/callback");

        //
        // [030] Request authorization code
        // [050] Consent not needed because Hobbits are connected
        // [070] Create auth code
        //
        var payload = new YouAuthAuthorizeRequest
        {
            ClientId = thirdParty,
            ClientType = ClientType.domain,
            ClientInfo = "",
            PermissionRequest = "",
            PublicKey = keyPair.PublicKeyJwkBase64Url(),
            State = "somestate",
            RedirectUri = $"https://{thirdParty}:{HttpsPort}/authorization/code/callback"
        };

        string remotePublicKey, remoteSalt;
        {
            var uri =
                new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                {
                    Query = payload.ToQueryString()
                }.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
            };

            var response = await apiClient.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

            var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
            var redirectUri = new Uri(location);
            Assert.That(redirectUri.Scheme, Is.EqualTo("https"));
            Assert.That(redirectUri.Host, Is.EqualTo($"{thirdParty}"));
            Assert.That(redirectUri.AbsolutePath, Is.EqualTo(finalRedirectUri.AbsolutePath));

            var qs = HttpUtility.ParseQueryString(redirectUri.Query);
            Console.WriteLine("qs = " + string.Join("; ",
                qs.AllKeys.Select(key => $"{key}={string.Join(",", qs.GetValues(key) ?? Array.Empty<string>())}")));

            var identity = qs[YouAuthDefaults.Identity]!;
            var state = qs[YouAuthDefaults.State]!;
            remotePublicKey = qs[YouAuthDefaults.PublicKey]!;
            remoteSalt = qs[YouAuthDefaults.Salt]!;

            Assert.That(identity, Is.EqualTo(hobbit));
            Assert.That(state, Is.EqualTo(payload.State));
            Assert.That(remotePublicKey, Is.Not.Null.And.Not.Empty);
            Assert.That(remoteSalt, Is.Not.Null.And.Not.Empty);
        }

        //
        // [90] Calculate shared secret and digtest for token exchange
        // [100] Exchange auth code for access token
        // [140] Return client access token to client
        //
        {
            var remotePublicKeyJwk = EccPublicKeyData.FromJwkBase64UrlPublicKey(remotePublicKey);
            var exchangeSecret = keyPair.GetEcdhSharedSecret(privateKey, remotePublicKeyJwk, Convert.FromBase64String(remoteSalt));
            var exchangeSecretDigest = SHA256.Create().ComputeHash(exchangeSecret.GetKey()).ToBase64();

            var uri = new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Token}");
            var tokenRequest = new YouAuthTokenRequest
            {
                SecretDigest = exchangeSecretDigest
            };
            var body = OdinSystemSerializer.Serialize(tokenRequest);

            var request = new HttpRequestMessage(HttpMethod.Post, uri.ToString())
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var content = await response.Content.ReadAsStringAsync();
            var token = OdinSystemSerializer.Deserialize<YouAuthTokenResponse>(content);

            Assert.That(token!.Base64SharedSecretCipher, Is.Not.Null.And.Not.Empty);
            Assert.That(token!.Base64SharedSecretIv, Is.Not.Null.And.Not.Empty);
            Assert.That(token.Base64ClientAuthTokenCipher, Is.Not.Null.And.Not.Empty);
            Assert.That(token.Base64ClientAuthTokenIv, Is.Not.Null.And.Not.Empty);

            var sharedSecretCipher = Convert.FromBase64String(token.Base64SharedSecretCipher!);
            var sharedSecretIv = Convert.FromBase64String(token.Base64SharedSecretIv!);
            var sharedSecret = AesCbc.Decrypt(sharedSecretCipher, exchangeSecret, sharedSecretIv);
            Assert.That(sharedSecret, Is.Not.Null.And.Not.Empty);

            var clientAuthTokenCipher = Convert.FromBase64String(token.Base64ClientAuthTokenCipher!);
            var clientAuthTokenIv = Convert.FromBase64String(token.Base64ClientAuthTokenIv!);
            var clientAuthToken = AesCbc.Decrypt(clientAuthTokenCipher, exchangeSecret, clientAuthTokenIv);
            Assert.That(clientAuthToken, Is.Not.Null.And.Not.Empty);
        }
    }

    //

    [Test]
    public async Task c1_app_registration_AuthorizeEndpointMustRedirectToRegistrationPageIfRegistrationIsNeeded()
    {
        const string hobbit = "sam.dotyou.cloud";
        var apiClient = Host.CreateClient();
        var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

        var appId = Guid.NewGuid();
        var driveAlias = Guid.NewGuid();
        var driveType = Guid.NewGuid();

        //
        // [010] Generate key pair
        //
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

        //
        // [030] Request authorization code
        //
        {
            //
            // Arrange
            //

            var appParams = GetAppParams(appId, driveAlias, driveType);
            var payload = new YouAuthAuthorizeRequest
            {
                ClientId = appId.ToString(),
                ClientType = ClientType.app,
                ClientInfo = "",
                PermissionRequest = OdinSystemSerializer.Serialize(appParams),
                PublicKey = keyPair.PublicKeyJwkBase64Url(),
                State = "somestate",
                RedirectUri = "https://app/authorization/code/callback"
            };

            var uri =
                new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                {
                    Query = payload.ToQueryString()
                }.Uri;

            var request = new HttpRequestMessage(HttpMethod.Get, uri.ToString())
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
            };

            //
            // Act
            //
            var response = await apiClient.SendAsync(request);

            //
            // Assert
            //
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

            // ... /owner/appreg
            var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
            var appRegUri = new Uri(location);
            Assert.That(appRegUri.Scheme, Is.EqualTo("https"));
            Assert.That(appRegUri.Host, Is.EqualTo($"{hobbit}"));
            Assert.That(appRegUri.AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.AppReg));

            // ... YouAuthAppParameters
            var appComponents = YouAuthAppParameters.FromQueryString(appRegUri.Query);
            Assert.That(appComponents.AppId, Is.EqualTo(appParams.AppId));
            Assert.That(appComponents.AppName, Is.EqualTo(appParams.AppName));
            Assert.That(appComponents.AppOrigin, Is.EqualTo(appParams.AppOrigin));
            Assert.That(appComponents.ClientFriendly, Is.EqualTo(appParams.ClientFriendly));
            Assert.That(appComponents.DrivesParam, Is.EqualTo(appParams.DrivesParam));

            // ... Return
            var returnUri = new Uri(appComponents.Return);
            Assert.That(returnUri.Scheme, Is.EqualTo(uri.Scheme));
            Assert.That(returnUri.Host, Is.EqualTo(uri.Host));
            Assert.That(returnUri.AbsolutePath, Is.EqualTo(uri.AbsolutePath));
            Assert.That(returnUri.Query, Is.Not.Null.And.Not.Empty);
        }
    }

    //

    [Test]
    public async Task c2_app_consent_AuthorizeEndpointMustRedirectRegisteredAppToConsentPageIfConsentIsNeeded()
    {
        const string hobbit = "sam.dotyou.cloud";
        var apiClient = Host.CreateClient();
        var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

        var appId = Guid.NewGuid().ToString();
        var driveAlias = Guid.NewGuid().ToString("N");
        var driveType = Guid.NewGuid().ToString("N");
        await RegisterApp(hobbit, Guid.Parse(appId), Guid.Parse(driveAlias), Guid.Parse(driveType));

        //
        // [010] Generate key pair
        //
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

        //
        // [030] Request authorization code
        //
        {
            //
            // Arrange
            //

            var driveParams = new[]
            {
                new
                {
                    a = driveAlias,
                    t = driveType,
                    n = "App name",
                    d = "App description",
                    p = 3 // permissions 3 = r/w
                },
            };
            var appParams = new YouAuthAppParameters
            {
                AppName = "Odin - Test App",
                AppOrigin = "dev.dotyou.cloud:3005",
                AppId = appId,
                ClientFriendly = "Firefox | macOS",
                DrivesParam = OdinSystemSerializer.Serialize(driveParams),
                Return = "backend-will-decide",
            };


            var payload = new YouAuthAuthorizeRequest
            {
                ClientId = appId,
                ClientType = ClientType.app,
                ClientInfo = "",
                PermissionRequest = OdinSystemSerializer.Serialize(appParams),
                PublicKey = keyPair.PublicKeyJwkBase64Url(),
                State = "somestate",
                RedirectUri = "https://app/authorization/code/callback"
            };

            var uri =
                new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                {
                    Query = payload.ToQueryString()
                }.Uri;

            var request = new HttpRequestMessage(HttpMethod.Get, uri.ToString())
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
            };

            //
            // Act
            //
            var response = await apiClient.SendAsync(request);

            //
            // Assert
            //
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

            // ... /owner/login/youauth
            var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
            var consentUri = new Uri(location);
            Assert.That(consentUri.Scheme, Is.EqualTo("https"));
            Assert.That(consentUri.Host, Is.EqualTo($"{hobbit}"));
            Assert.That(consentUri.AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Consent));

            // ... ?returnUrl= ...
            var qs = YouAuthTestHelper.ParseQueryString(location);
            var returnUrl = new Uri(qs["returnUrl"]);
            Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}"));

            // ... returnUrl components:
            var returnUrlComponents = YouAuthAuthorizeRequest.FromQueryString(returnUrl.Query);
            Assert.That(returnUrlComponents.ClientId, Is.EqualTo(payload.ClientId));
            Assert.That(returnUrlComponents.ClientType, Is.EqualTo(payload.ClientType));
            Assert.That(returnUrlComponents.PermissionRequest, Is.EqualTo(payload.PermissionRequest));
            Assert.That(returnUrlComponents.RedirectUri, Is.EqualTo(payload.RedirectUri));
            Assert.That(returnUrlComponents.State, Is.EqualTo(payload.State));
            Assert.That(returnUrlComponents.ClientInfo, Is.EqualTo(payload.ClientInfo));
            Assert.That(returnUrlComponents.PublicKey, Is.EqualTo(payload.PublicKey));
        }
    }

    //

    [Test]
    public async Task c3_app_consent_AuthorizeMustSkipConsentForAppsOnOwner()
    {
        const string hobbit = "sam.dotyou.cloud";
        var apiClient = Host.CreateClient();
        var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

        var appId = Guid.NewGuid().ToString();
        var driveAlias = Guid.NewGuid().ToString("N");
        var driveType = Guid.NewGuid().ToString("N");
        await RegisterApp(hobbit, Guid.Parse(appId), Guid.Parse(driveAlias), Guid.Parse(driveType));

        //
        // [010] Generate key pair
        //
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

        //
        // [030] Request authorization code
        //
        {
            //
            // Arrange
            //

            var driveParams = new[]
            {
                new
                {
                    a = driveAlias,
                    t = driveType,
                    n = "App name",
                    d = "App description",
                    p = 3 // permissions 3 = r/w
                },
            };
            var appParams = new YouAuthAppParameters
            {
                AppName = "Odin - Test App",
                AppOrigin = "dev.dotyou.cloud:3005",
                AppId = appId,
                ClientFriendly = "Firefox | macOS",
                DrivesParam = OdinSystemSerializer.Serialize(driveParams),
                Return = "backend-will-decide",
            };


            var payload = new YouAuthAuthorizeRequest
            {
                ClientId = appId,
                ClientType = ClientType.app,
                ClientInfo = "",
                PermissionRequest = OdinSystemSerializer.Serialize(appParams),
                PublicKey = keyPair.PublicKeyJwkBase64Url(),
                State = "somestate",
                RedirectUri = $"https://{hobbit}:{HttpsPort}/app/authorization/code/callback"
            };

            var uri =
                new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                {
                    Query = payload.ToQueryString()
                }.Uri;

            var request = new HttpRequestMessage(HttpMethod.Get, uri.ToString())
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
            };

            //
            // Act
            //
            var response = await apiClient.SendAsync(request);

            //
            // Assert
            //
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

            var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
            var redirectUri = new Uri(location);
            Assert.That(redirectUri.Scheme, Is.EqualTo("https"));
            Assert.That(redirectUri.Host, Is.EqualTo($"{hobbit}"));
            Assert.That(redirectUri.AbsoluteUri, Does.StartWith($"https://{hobbit}:{HttpsPort}/app/authorization/code/callback"));

            var qs = HttpUtility.ParseQueryString(redirectUri.Query);
            Console.WriteLine("qs = " + string.Join("; ",
                qs.AllKeys.Select(key => $"{key}={string.Join(",", qs.GetValues(key) ?? Array.Empty<string>())}")));

            var identity = qs[YouAuthDefaults.Identity]!;
            var state = qs[YouAuthDefaults.State]!;
            var remotePublicKey = qs[YouAuthDefaults.PublicKey]!;
            var remoteSalt = qs[YouAuthDefaults.Salt]!;

            Assert.That(identity, Is.EqualTo(hobbit));
            Assert.That(state, Is.EqualTo(payload.State));
            Assert.That(remotePublicKey, Is.Not.Null.And.Not.Empty);
            Assert.That(remoteSalt, Is.Not.Null.And.Not.Empty);
        }
    }


    //

    [Test]
    public async Task c4_app_exchange_WithExplicitConsentItShouldExchangeAuthorizationCodeForToken()
    {
        const string hobbit = "sam.dotyou.cloud";
        var apiClient = Host.CreateClient();
        var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

        var appId = Guid.NewGuid();
        var driveAlias = Guid.NewGuid();
        var driveType = Guid.NewGuid();
        await RegisterApp(hobbit, appId, driveAlias, driveType);

        //
        // [010] Generate key pair
        //
        var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
        var keyPair = new EccFullKeyData(privateKey, EccKeySize.P384, 1);

        var finalRedirectUri = new Uri("https://app/authorization/code/callback");
        var appParams = GetAppParams(appId, driveAlias, driveType);
        var payload = new YouAuthAuthorizeRequest
        {
            ClientId = appParams.AppId,
            ClientType = ClientType.app,
            ClientInfo = "",
            PermissionRequest = OdinSystemSerializer.Serialize(appParams),
            PublicKey = keyPair.PublicKeyJwkBase64Url(),
            State = "somestate",
            RedirectUri = finalRedirectUri.ToString()
        };

        //
        // [030] Request authorization code
        //
        Uri returnUrl;
        {
            var uri =
                new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                {
                    Query = payload.ToQueryString()
                }.Uri;

            var request = new HttpRequestMessage(HttpMethod.Get, uri.ToString())
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
            };

            //
            // Act
            //
            var response = await apiClient.SendAsync(request);

            //
            // Assert
            //
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

            // ... /owner/youauth/consent
            var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
            var consentUri = new Uri(location);
            Assert.That(consentUri.Scheme, Is.EqualTo("https"));
            Assert.That(consentUri.Host, Is.EqualTo($"{hobbit}"));
            Assert.That(consentUri.AbsolutePath, Is.EqualTo(OwnerFrontendPathConstants.Consent));

            // ... ?returnUrl= ...
            var qs = YouAuthTestHelper.ParseQueryString(location);
            returnUrl = new Uri(qs["returnUrl"]);
            Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}"));

            // ... returnUrl components:
            var returnUrlComponents = YouAuthAuthorizeRequest.FromQueryString(returnUrl.Query);
            Assert.That(returnUrlComponents.ClientId, Is.EqualTo(payload.ClientId));
            Assert.That(returnUrlComponents.ClientType, Is.EqualTo(payload.ClientType));
            Assert.That(returnUrlComponents.PermissionRequest, Is.EqualTo(payload.PermissionRequest));
            Assert.That(returnUrlComponents.RedirectUri, Is.EqualTo(payload.RedirectUri));
            Assert.That(returnUrlComponents.State, Is.EqualTo(payload.State));
            Assert.That(returnUrlComponents.ClientInfo, Is.EqualTo(payload.ClientInfo));
            Assert.That(returnUrlComponents.PublicKey, Is.EqualTo(payload.PublicKey));
        }

        //
        // [050 - 055] Consent needed, give consent
        //
        {
            //
            // The frontend consent page (/owner/youauth/consent) shows a form
            // with app name, app description and scopes the user must authorize.
            //
            // When the user consents, i.e. clicks OK, the form does a POST to the backend authorize endpoint:
            //

            var uri = new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}");

            var request = new HttpRequestMessage(HttpMethod.Post, uri.ToString())
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { YouAuthAuthorizeConsentGiven.ReturnUrlName, returnUrl.ToString() }
                })
            };

            var response = await apiClient.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

            var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
            var authorizeUri = new Uri(location);
            Assert.That(authorizeUri.Scheme, Is.EqualTo("https"));
            Assert.That(authorizeUri.Host, Is.EqualTo($"{hobbit}"));
            Assert.That(authorizeUri.AbsolutePath, Is.EqualTo(OwnerApiPathConstants.YouAuthV1Authorize));
        }

        //
        // [070] Create auth code
        // [080] return auth code to client
        //
        string remotePublicKey, remoteSalt;
        {
            var uri =
                new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Authorize}")
                {
                    Query = payload.ToQueryString()
                }.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
            };

            var response = await apiClient.SendAsync(request);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));

            var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
            var redirectUri = new Uri(location);
            Assert.That(redirectUri.Scheme, Is.EqualTo(finalRedirectUri.Scheme));
            Assert.That(redirectUri.Host, Is.EqualTo(finalRedirectUri.Host));
            Assert.That(redirectUri.AbsolutePath, Is.EqualTo(finalRedirectUri.AbsolutePath));

            var qs = HttpUtility.ParseQueryString(redirectUri.Query);
            Console.WriteLine("qs = " + string.Join("; ",
                qs.AllKeys.Select(key => $"{key}={string.Join(",", qs.GetValues(key) ?? Array.Empty<string>())}")));

            var identity = qs[YouAuthDefaults.Identity]!;
            var state = qs[YouAuthDefaults.State]!;
            remotePublicKey = qs[YouAuthDefaults.PublicKey]!;
            remoteSalt = qs[YouAuthDefaults.Salt]!;

            Assert.That(identity, Is.EqualTo(hobbit));
            Assert.That(state, Is.EqualTo(payload.State));
            Assert.That(remotePublicKey, Is.Not.Null.And.Not.Empty);
            Assert.That(remoteSalt, Is.Not.Null.And.Not.Empty);
        }

        //
        // [90] Calculate shared secret and digtest for token exchange
        // [100] Exchange auth code for access token
        // [140] Return client access token to client
        //
        {
            var remotePublicKeyJwk = EccPublicKeyData.FromJwkBase64UrlPublicKey(remotePublicKey);
            var exchangeSecret = keyPair.GetEcdhSharedSecret(privateKey, remotePublicKeyJwk, Convert.FromBase64String(remoteSalt));
            var exchangeSecretDigest = SHA256.Create().ComputeHash(exchangeSecret.GetKey()).ToBase64();

            var uri = new UriBuilder($"https://{hobbit}:{HttpsPort}{OwnerApiPathConstants.YouAuthV1Token}");
            var tokenRequest = new YouAuthTokenRequest
            {
                SecretDigest = exchangeSecretDigest
            };
            var body = OdinSystemSerializer.Serialize(tokenRequest);

            var request = new HttpRequestMessage(HttpMethod.Post, uri.ToString())
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var content = await response.Content.ReadAsStringAsync();
            var token = OdinSystemSerializer.Deserialize<YouAuthTokenResponse>(content);

            Assert.That(token!.Base64SharedSecretCipher, Is.Not.Null.And.Not.Empty);
            Assert.That(token!.Base64SharedSecretIv, Is.Not.Null.And.Not.Empty);
            Assert.That(token.Base64ClientAuthTokenCipher, Is.Not.Null.And.Not.Empty);
            Assert.That(token.Base64ClientAuthTokenIv, Is.Not.Null.And.Not.Empty);

            var sharedSecretCipher = Convert.FromBase64String(token.Base64SharedSecretCipher!);
            var sharedSecretIv = Convert.FromBase64String(token.Base64SharedSecretIv!);
            var sharedSecret = AesCbc.Decrypt(sharedSecretCipher, exchangeSecret, sharedSecretIv);
            Assert.That(sharedSecret, Is.Not.Null.And.Not.Empty);

            var clientAuthTokenCipher = Convert.FromBase64String(token.Base64ClientAuthTokenCipher!);
            var clientAuthTokenIv = Convert.FromBase64String(token.Base64ClientAuthTokenIv!);
            var clientAuthToken = AesCbc.Decrypt(clientAuthTokenCipher, exchangeSecret, clientAuthTokenIv);
            Assert.That(clientAuthToken, Is.Not.Null.And.Not.Empty);

            var queryBatchResponse = await QueryBatch(
                hobbit,
                clientAuthToken.ToBase64(),
                sharedSecret.ToBase64(),
                driveAlias.ToString(),
                driveType.ToString());

            Assert.That(queryBatchResponse.QueryTime.milliseconds, Is.GreaterThan(0));
        }
    }

    //

    // -------------------------------------------------------------------------------------------
    // Helpers, carried over from the original's YouAuthIntegrationTestBase (one subclass, so they
    // live on the fixture here).
    // -------------------------------------------------------------------------------------------

    // Authenticate and return owner cookie and shared secret
    private async Task<(string, string)> AuthenticateOwnerReturnOwnerCookieAndSharedSecret(string identity)
    {
        var apiClient = Host.CreateClient();

        // Step 1:
        // Check owner cookie (we don't send any).
        // Backend will return false and we move on to owner-authentication in step 2.
        //
        // https://sam.dotyou.cloud/api/owner/v1/authentication/verifyToken
        {
            var response = await apiClient.GetAsync($"https://{identity}:{HttpsPort}/api/owner/v1/authentication/verifyToken");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var json = await response.Content.ReadAsStringAsync();
            Assert.That(YouAuthTestHelper.Deserialize<bool>(json), Is.False);
        }

        // Step 2
        // Get nonce for authentication
        //
        // https://sam.dotyou.cloud/api/owner/v1/authentication/nonce
        NonceData nonceData;
        {
            var response = await apiClient.GetAsync($"https://{identity}:{HttpsPort}/api/owner/v1/authentication/nonce");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var json = await response.Content.ReadAsStringAsync();
            nonceData = YouAuthTestHelper.Deserialize<NonceData>(json);
            Assert.That(nonceData.Nonce64, Is.Not.Null.And.Not.Empty);
        }

        // Step 3:
        // Authenticate
        //
        // https://sam.dotyou.cloud/api/owner/v1/authentication
        string ownerCookie;
        string sharedSecret;
        {
            var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
            var passwordReply = PasswordDataManager.CalculatePasswordReply(YouAuthTestHelper.Password, nonceData, clientEccFullKey);
            var json = YouAuthTestHelper.Serialize(passwordReply);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await apiClient.PostAsync($"https://{identity}:{HttpsPort}/api/owner/v1/authentication", content);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            // Shared secret from response
            json = await response.Content.ReadAsStringAsync();
            var ownerAuthResult = YouAuthTestHelper.Deserialize<OwnerAuthenticationResult>(json);
            sharedSecret = Convert.ToBase64String(ownerAuthResult.SharedSecret);
            Assert.That(sharedSecret, Is.Not.Null.And.Not.Empty);

            // Owner cookie from response
            var cookies = response.GetCookies();
            ownerCookie = cookies[YouAuthTestHelper.OwnerCookieName];
            Assert.That(ownerCookie, Is.Not.Null.And.Not.Empty);
        }

        // Step 4:
        // Check owner cookie again (this time we have it, so send it)
        //
        // https://sam.dotyou.cloud/api/owner/v1/authentication/verifyToken
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://{identity}:{HttpsPort}/api/owner/v1/authentication/verifyToken")
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } }
            };
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var json = await response.Content.ReadAsStringAsync();
            Assert.That(YouAuthTestHelper.Deserialize<bool>(json), Is.True);
        }

        return (ownerCookie, sharedSecret);
    }

    //

    private async Task<RedactedAppRegistration> RegisterApp(
        string identity,
        Guid appId,
        Guid driveAlias,
        Guid driveType)
    {
        var ownerClient = await LoginAsOwner(identity);

        var drive = new TargetDrive
        {
            Alias = driveAlias,
            Type = driveType
        };

        await ownerClient.Admin.CreateDrive(drive, "Test Drive", allowAnonymousReads: false, ownerOnly: false,
            allowSubscriptions: false);

        var appPermissionsGrant = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = drive,
                        Permission = DrivePermission.All
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.All)
        };

        var appRegistration = await ownerClient.Admin.RegisterApp(appId, appPermissionsGrant);

        return appRegistration.Content!;
    }

    //

    /// <summary>
    /// The four-identity mesh <c>Scaffold.Scenarios.CreateConnectedHobbits</c> built. See the class
    /// remarks for what the V2 equivalent leaves out.
    /// </summary>
    private async Task ConnectHobbits()
    {
        var targetDrive = TargetDrive.NewTargetDrive();

        var hobbits = new List<OwnerSession>();
        foreach (var identity in HostIdentities)
        {
            hobbits.Add(await LoginAsOwner(identity));
        }

        await PeerFlow.ConnectAllAsync(hobbits, targetDrive);
    }

    //

    private static YouAuthAppParameters GetAppParams(Guid appId, Guid driveAlias, Guid driveType)
    {
        var driveParams = new[]
        {
            new
            {
                a = driveAlias.ToString("N"),
                t = driveType.ToString("N"),
                n = "Short app name",
                d = "Longer app description",
                p = 3
            },
        };
        var appParams = new YouAuthAppParameters
        {
            AppName = "Odin - Some App",
            AppOrigin = "dev.dotyou.cloud:3005",
            AppId = appId.ToString(),
            ClientFriendly = "Firefox | macOS",
            DrivesParam = OdinSystemSerializer.Serialize(driveParams),
            Return = "backend-will-decide",
        };

        return appParams;
    }

    //

    private async Task<QueryBatchResponse> QueryBatch(
        string domain,
        string clientAuthToken,
        string sharedSecret,
        string driveAlias,
        string driveType)
    {
        var catCookie = new Cookie("BX0900", clientAuthToken);
        var client = Host.CreateClient();
        client.DefaultRequestHeaders.Add("X-ODIN-FILE-SYSTEM-TYPE", "Standard");
        client.DefaultRequestHeaders.Add("Cookie", catCookie.ToString());

        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["maxRecords"] = "1000";
        qs["includeMetadataHeader"] = "true";
        qs["alias"] = driveAlias;
        qs["type"] = driveType;
        qs["fileState"] = "1";

        var url = $"https://{domain}:{HttpsPort}/api/apps/v1/drive/query/batch?{qs}";

        url = YouAuthTestHelper.UriWithEncryptedQueryString(url, sharedSecret);
        var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == HttpStatusCode.OK)
        {
            try
            {
                return YouAuthTestHelper.DecryptContent<QueryBatchResponse>(content, sharedSecret);
            }
            catch (Exception e)
            {
                throw new Exception($"Oh no {(int)response.StatusCode}: {e.Message}", e);
            }
        }

        string json;
        try
        {
            json = YouAuthTestHelper.DecryptContent(content, sharedSecret);
        }
        catch (Exception e)
        {
            throw new Exception($"Oh no {(int)response.StatusCode}: {content}", e);
        }
        throw new Exception($"Oh no {(int)response.StatusCode}: {json}");
    }
}
