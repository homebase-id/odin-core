#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Core.Services.Authentication.Owner;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Hosting.Controllers.Home.Auth;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;

namespace Odin.Hosting.Tests.YouAuthApi.IntegrationTests
{
    //
    // Flow being tested:
    // https://github.com/YouFoundation/stories-and-architecture-docs/blob/master/concepts/YouAuth/unified-authorization.md
    //

    public class YouAuthIntegrationTests : YouAuthIntegrationTestBase
    {
        [Test]
        public async Task a_AuthorizeEndpointMustRedirectToLogonIfUserNotAuthenticated()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            const string thirdParty = "frodo.dotyou.cloud";

            //
            // [010] Generate key pair
            //
            var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var keyPair = new EccFullKeyData(privateKey, 1);

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
                    RedirectUri = $"https://{thirdParty}/authorization/code/callback"
                };

                var uri =
                    new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}")
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
                Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}"));

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
        public async Task c1_domain_AuthorizeEndpointMustRedirectToConsentPageIfConsentIsNeeded()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            await DisconnectHobbits(TestIdentities.Frodo, TestIdentities.Samwise);

            //
            // [010] Generate key pair
            //
            var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var keyPair = new EccFullKeyData(privateKey, 1);

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
                    RedirectUri = $"https://{thirdParty}/authorization/code/callback"
                };

                var uri =
                    new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}")
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
                Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}"));

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
        public async Task b_app1_registration_AuthorizeEndpointMustRedirectToRegistrationPageIfRegistrationIsNeeded()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            //
            // [010] Generate key pair
            //
            var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var keyPair = new EccFullKeyData(privateKey, 1);

            //
            // [030] Request authorization code
            //
            {
                //
                // Arrange
                //

                var appId = "32f0bdbf-017f-4fc0-8004-2d4631182d1e"; // photos
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
                    RedirectUri = $"https://app/authorization/code/callback"
                };

                var uri =
                    new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}")
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
        public async Task b_app2_consent_AuthorizeEndpointMustRedirectRegisteredAppToConsentPageIfConsentIsNeeded()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            var appId = "32f0bdbf-017f-4fc0-8004-2d4631182d1e"; // photos
            await RegisterApp(hobbit, Guid.Parse(appId));

            //
            // [010] Generate key pair
            //
            var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var keyPair = new EccFullKeyData(privateKey, 1);

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
                    RedirectUri = $"https://app/authorization/code/callback"
                };

                var uri =
                    new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}")
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
                Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}"));

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
        public async Task b_app3_exchange_WithExplicitConsentItShouldExchangeAuthorizationCodeForToken()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            //
            // [010] Generate key pair
            //
            var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var keyPair = new EccFullKeyData(privateKey, 1);

            var finalRedirectUri = new Uri("https://app/authorization/code/callback");
            var appParams = GetAppPhotosParams();
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

            var appId = appParams.AppId;
            await RegisterApp(hobbit, Guid.Parse(appId));

            //
            // [030] Request authorization code
            //
            Uri returnUrl;
            {
                var uri =
                    new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}")
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
                Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}"));

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
            // [050] Consent needed
            //
            {
                //
                // The frontend consent page (/owner/youauth/consent) shows a form
                // with app name, app description and scopes the user must authorize.
                //
                // When the user consents, i.e. clicks OK, the form does a POST to the backend authorize endpoint:
                //

                var uri = new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}");

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
            byte[] remotePublicKey, remoteSalt;
            {
                var uri =
                    new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}")
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
                var identity = qs[YouAuthDefaults.Identity]!;
                var state = qs[YouAuthDefaults.State]!;
                remotePublicKey = Convert.FromBase64String(qs[YouAuthDefaults.PublicKey]!);
                remoteSalt = Convert.FromBase64String(qs[YouAuthDefaults.Salt]!);

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
                var remotePublicKeyJwk = EccPublicKeyData.FromJwkBase64UrlPublicKey(Convert.ToBase64String(remotePublicKey));
                var exchangeSecret = keyPair.GetEcdhSharedSecret(privateKey, remotePublicKeyJwk, remoteSalt);
                var exchangeSecretDigest = SHA256.Create().ComputeHash(exchangeSecret.GetKey()).ToBase64();

                var uri = new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Token}");
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
                var sharedSecret = AesCbc.Decrypt(sharedSecretCipher, ref exchangeSecret, sharedSecretIv);
                Assert.That(sharedSecret, Is.Not.Null.And.Not.Empty);

                var clientAuthTokenCipher = Convert.FromBase64String(token.Base64ClientAuthTokenCipher!);
                var clientAuthTokenIv = Convert.FromBase64String(token.Base64ClientAuthTokenIv!);
                var clientAuthToken = AesCbc.Decrypt(clientAuthTokenCipher, ref exchangeSecret, clientAuthTokenIv);
                Assert.That(clientAuthToken, Is.Not.Null.And.Not.Empty);

                // SEB:TODO test that token works
            }
        }

        //

        [Test]
        [Ignore("Needs implementation")]
        public void todo_app_AlwaysGetConsentWhenClientTypeIsApp()
        {
            // An app always requires consent from the user.
            Assert.Fail("SEB:TODO implement me, lazy hooman!");
        }

        [Test]
        [Ignore("Needs implementation")]
        public void todo_domain_GetConsentWhenConsentNotAlreadyGiven()
        {
            // Consent will be necessitated for any domain unless Sam has previously granted approval for automatic
            // consent on that specific domain.
            // If permission_request is set, and is requesting permissions not already granted,
            // then a consent is always required.
            Assert.Fail("SEB:TODO implement me, lazy hooman!");
        }

        //

        [Test]
        [Ignore("Needs implementation")]
        public void todo_domain_IgnoreConsentWhenClientTypeIsDomainAndConsentIsAlreadyGiven()
        {
            // Consent will be necessitated for any domain unless Sam has previously granted approval for automatic
            // consent on that specific domain.
            // If permission_request is set, and is requesting permissions not already granted,
            // then a consent is always required.
            Assert.Fail("SEB:TODO implement me, lazy hooman!");
        }

        //

        [Test]
        [Ignore("Needs implementation")]
        public void todo_domain_GetConsentWhenClientTypeIsDomainAndConsentWasPreviouslyGivenButPermissionRequestHasChanged()
        {
            // Consent will be necessitated for any domain unless Sam has previously granted approval for automatic
            // consent on that specific domain.
            // If permission_request is set, and is requesting permissions not already granted,
            // then a consent is always required.
            Assert.Fail("SEB:TODO implement me, lazy hooman!");
        }

        //

        [Test]
        public async Task c4_domain_WithImplicitConsentClientAccessTokenShouldBeDeliveredAsJsonResponse()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            await ConnectHobbits();

            //
            // [010] Generate key pair
            //
            var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var keyPair = new EccFullKeyData(privateKey, 1);

            const string thirdParty = "frodo.dotyou.cloud";
            var finalRedirectUri = new Uri($"https://{thirdParty}/authorization/code/callback");

            //
            // [030] Request authorization code
            // [070] Create auth code
            // [080] return auth code to client
            //
            byte[] remotePublicKey, remoteSalt;
            {
                var payload = new YouAuthAuthorizeRequest
                {
                    ClientId = thirdParty,
                    ClientType = ClientType.domain,
                    ClientInfo = "",
                    PermissionRequest = "",
                    PublicKey = keyPair.PublicKeyJwkBase64Url(),
                    State = "somestate",
                    RedirectUri = $"https://{thirdParty}/authorization/code/callback"
                };

                var uri =
                    new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}")
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
                var identity = qs[YouAuthDefaults.Identity]!;
                var state = qs[YouAuthDefaults.State]!;
                remotePublicKey = Convert.FromBase64String(qs[YouAuthDefaults.PublicKey]!);
                remoteSalt = Convert.FromBase64String(qs[YouAuthDefaults.Salt]!);

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
                var remotePublicKeyJwk = EccPublicKeyData.FromJwkBase64UrlPublicKey(Convert.ToBase64String(remotePublicKey));
                var exchangeSecret = keyPair.GetEcdhSharedSecret(privateKey, remotePublicKeyJwk, remoteSalt);
                var exchangeSecretDigest = SHA256.Create().ComputeHash(exchangeSecret.GetKey()).ToBase64();

                var uri = new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Token}");
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
                sharedSecret = AesCbc.Decrypt(sharedSecretCipher, ref exchangeSecret, sharedSecretIv);
                Assert.That(sharedSecret, Is.Not.Null.And.Not.Empty);

                var clientAuthTokenCipher = Convert.FromBase64String(token.Base64ClientAuthTokenCipher!);
                var clientAuthTokenIv = Convert.FromBase64String(token.Base64ClientAuthTokenIv!);
                clientAuthToken = AesCbc.Decrypt(clientAuthTokenCipher, ref exchangeSecret, clientAuthTokenIv);
                Assert.That(clientAuthToken, Is.Not.Null.And.Not.Empty);
            }

            // Access resource using cat and shared secret
            {
                var catBase64 = Convert.ToBase64String(clientAuthToken);
                var uri = YouAuthTestHelper.UriWithEncryptedQueryString($"https://{hobbit}{HomeApiPathConstants.AuthV1}/{HomeApiPathConstants.PingMethodName}?text=helloworld", sharedSecret);
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
        public async Task c6_domain_WithExplicitConsentClientAccessTokenShouldBeDeliveredAsJsonResponse()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, _) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            await DisconnectHobbits(TestIdentities.Frodo, TestIdentities.Samwise);

            //
            // [010] Generate key pair
            //
            var privateKey = new SensitiveByteArray(Guid.NewGuid().ToByteArray());
            var keyPair = new EccFullKeyData(privateKey, 1);

            Uri returnUrl;
            const string thirdParty = "frodo.dotyou.cloud";
            var finalRedirectUri = new Uri($"https://{thirdParty}/authorization/code/callback");

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
                RedirectUri = $"https://{thirdParty}/authorization/code/callback"
            };
            {
                var uri =
                    new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}")
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
                Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}"));
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

                var uri = new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}");

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
                authorizeUri = new Uri(location);
                Assert.That(authorizeUri.Scheme, Is.EqualTo("https"));
                Assert.That(authorizeUri.Host, Is.EqualTo($"{hobbit}"));
                Assert.That(authorizeUri.AbsolutePath, Is.EqualTo(OwnerApiPathConstants.YouAuthV1Authorize));
            }

            //
            // [070] Create auth code
            //
            byte[] remotePublicKey, remoteSalt;
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
                var identity = qs[YouAuthDefaults.Identity]!;
                var state = qs[YouAuthDefaults.State]!;
                remotePublicKey = Convert.FromBase64String(qs[YouAuthDefaults.PublicKey]!);
                remoteSalt = Convert.FromBase64String(qs[YouAuthDefaults.Salt]!);

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
                var remotePublicKeyJwk = EccPublicKeyData.FromJwkBase64UrlPublicKey(Convert.ToBase64String(remotePublicKey));
                var exchangeSecret = keyPair.GetEcdhSharedSecret(privateKey, remotePublicKeyJwk, remoteSalt);
                var exchangeSecretDigest = SHA256.Create().ComputeHash(exchangeSecret.GetKey()).ToBase64();

                var uri = new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Token}");
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
                sharedSecret = AesCbc.Decrypt(sharedSecretCipher, ref exchangeSecret, sharedSecretIv);
                Assert.That(sharedSecret, Is.Not.Null.And.Not.Empty);

                var clientAuthTokenCipher = Convert.FromBase64String(token.Base64ClientAuthTokenCipher!);
                var clientAuthTokenIv = Convert.FromBase64String(token.Base64ClientAuthTokenIv!);
                clientAuthToken = AesCbc.Decrypt(clientAuthTokenCipher, ref exchangeSecret, clientAuthTokenIv);
                Assert.That(clientAuthToken, Is.Not.Null.And.Not.Empty);
            }

            // Access resource using cat and shared secret
            {
                var catBase64 = Convert.ToBase64String(clientAuthToken);
                var uri = YouAuthTestHelper.UriWithEncryptedQueryString($"https://{hobbit}{HomeApiPathConstants.AuthV1}/{HomeApiPathConstants.PingMethodName}?text=helloworld", sharedSecret);
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
    }
}