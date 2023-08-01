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
using Odin.Core.Serialization;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Hosting.Controllers.OwnerToken;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;

namespace Odin.Hosting.Tests.YouAuthApi.IntegrationTests.Unified
{
    //
    // Flow being tested:
    // https://github.com/YouFoundation/stories-and-architecture-docs/blob/master/concepts/YouAuth/unified-authorization.md
    //

    public class UnifiedTests : YouAuthIntegrationTestBase
    {
        [Test]
        public async Task a_AuthorizeEndpointMustRedirectToLogonIfUserNotAuthenticated()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();

            //
            // [010] Generate code verifier
            //
            var codeVerifier = Guid.NewGuid().ToString();
            var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();

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
                    CodeChallenge = codeChallenge,
                    PermissionRequest = "identity:read identity:write",
                    RedirectUri = $"https://{hobbit}/foo/code/callback"
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
                Assert.That(loginUri.AbsolutePath, Is.EqualTo("/owner/login/youauth"));

                // ... ?returnUrl= ...
                var qs = YouAuthTestHelper.ParseQueryString(location);
                var returnUrl = new Uri(qs["returnUrl"]);
                Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}"));

                // ... returnUrl components:
                var returnUrlComponents = YouAuthAuthorizeRequest.FromQueryString(returnUrl.Query);
                Assert.That(returnUrlComponents.ClientId, Is.EqualTo(payload.ClientId));
                Assert.That(returnUrlComponents.ClientType, Is.EqualTo(payload.ClientType));
                Assert.That(returnUrlComponents.CodeChallenge, Is.EqualTo(payload.CodeChallenge));
                Assert.That(returnUrlComponents.PermissionRequest, Is.EqualTo(payload.PermissionRequest));
                Assert.That(returnUrlComponents.RedirectUri, Is.EqualTo(payload.RedirectUri));
                Assert.That(returnUrlComponents.ClientInfo, Is.EqualTo(payload.ClientInfo));
            }
        }

        //

        [Test]
        public async Task b_domain_AuthorizeEndpointMustRedirectToConsentIfDomainNotConnectedToIdentity()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, ownerSharedSecret) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            //
            // [010] Generate code verifier
            //
            var codeVerifier = Guid.NewGuid().ToString();
            var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();

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
                    CodeChallenge = codeChallenge,
                    PermissionRequest = "",
                    RedirectUri = $"https://{thirdParty}/foo/code/callback"
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

                // ... /owner/login/youauth
                var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                var consentUri = new Uri(location);
                Assert.That(consentUri.Scheme, Is.EqualTo("https"));
                Assert.That(consentUri.Host, Is.EqualTo($"{hobbit}"));
                Assert.That(consentUri.AbsolutePath, Is.EqualTo("/owner/consent"));

                // ... ?returnUrl= ...
                var qs = YouAuthTestHelper.ParseQueryString(location);
                var returnUrl = new Uri(qs["returnUrl"]);
                Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}"));

                // ... returnUrl components:
                var returnUrlComponents = YouAuthAuthorizeRequest.FromQueryString(returnUrl.Query);
                Assert.That(returnUrlComponents.ClientId, Is.EqualTo(payload.ClientId));
                Assert.That(returnUrlComponents.ClientType, Is.EqualTo(payload.ClientType));
                Assert.That(returnUrlComponents.CodeChallenge, Is.EqualTo(payload.CodeChallenge));
                Assert.That(returnUrlComponents.PermissionRequest, Is.EqualTo(payload.PermissionRequest));
                Assert.That(returnUrlComponents.RedirectUri, Is.EqualTo(payload.RedirectUri));
                Assert.That(returnUrlComponents.ClientInfo, Is.EqualTo(payload.ClientInfo));
            }
        }

        //

        [Test]
        public async Task b_domain_ConnectedDomainAndIdentityShouldYieldAuthorizationCodeWithoutConsentPrompt()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, ownerSharedSecret) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            //
            // [010] Generate code verifier
            //
            var codeVerifier = Guid.NewGuid().ToString();
            var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();

            const string thirdParty = "frodo.dotyou.cloud";
            var finalRedirectUri = new Uri($"https://{thirdParty}/foo/code/callback");

            //
            // [030] Request authorization code
            //
            {
                var payload = new YouAuthAuthorizeRequest
                {
                    ClientId = thirdParty,
                    ClientType = ClientType.domain,
                    ClientInfo = "",
                    CodeChallenge = codeChallenge,
                    PermissionRequest = "",
                    RedirectUri = $"https://{thirdParty}/foo/code/callback"
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
                Assert.That(qs["code"], Is.Not.Null.And.Not.Empty);
            }
        }

        //

        [Test]
        public async Task b_app_AuthorizeEndpointMustRedirectToConsentPageIfConsentNotAlreadyGiven()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, ownerSharedSecret) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            //
            // [010] Generate code verifier
            //
            var codeVerifier = Guid.NewGuid().ToString();
            var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();

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
                    CodeChallenge = codeChallenge,
                    PermissionRequest = "identity:read identity:write",
                    RedirectUri = $"https://{hobbit}/foo/code/callback"
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

                // ... /owner/login/youauth
                var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                var consentUri = new Uri(location);
                Assert.That(consentUri.Scheme, Is.EqualTo("https"));
                Assert.That(consentUri.Host, Is.EqualTo($"{hobbit}"));
                Assert.That(consentUri.AbsolutePath, Is.EqualTo("/owner/youauth/authorize"));

                // ... ?returnUrl= ...
                var qs = YouAuthTestHelper.ParseQueryString(location);
                var returnUrl = new Uri(qs["returnUrl"]);
                Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}"));

                // ... returnUrl components:
                var returnUrlComponents = YouAuthAuthorizeRequest.FromQueryString(returnUrl.Query);
                Assert.That(returnUrlComponents.ClientId, Is.EqualTo(payload.ClientId));
                Assert.That(returnUrlComponents.ClientType, Is.EqualTo(payload.ClientType));
                Assert.That(returnUrlComponents.CodeChallenge, Is.EqualTo(payload.CodeChallenge));
                Assert.That(returnUrlComponents.PermissionRequest, Is.EqualTo(payload.PermissionRequest));
                Assert.That(returnUrlComponents.RedirectUri, Is.EqualTo(payload.RedirectUri));
                Assert.That(returnUrlComponents.ClientInfo, Is.EqualTo(payload.ClientInfo));
            }
        }

        //

        [Test]
        public async Task c1_app_GivingConsentShouldYieldAuthorizationCode()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var appId = Guid.NewGuid();
            var appRegistration = RegisterApp(hobbit, appId);
            var (ownerCookie, ownerSharedSecret) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            //
            // [010] Generate code verifier
            //
            var codeVerifier = Guid.NewGuid().ToString();
            var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();

            Uri returnUrl;
            var finalRedirectUri = new Uri($"https://{hobbit}/foo/code/callback");

            //
            // [030] Request authorization code
            //
            {
                var payload = new YouAuthAuthorizeRequest
                {
                    ClientId = appId.ToString(),
                    ClientType = ClientType.app,
                    ClientInfo = "My Awesome App",
                    CodeChallenge = codeChallenge,
                    PermissionRequest = "identity:read identity:write",
                    RedirectUri = $"https://{hobbit}/foo/code/callback"
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

                // ... /owner/youauth/authorize
                var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                var consentUri = new Uri(location);
                Assert.That(consentUri.Scheme, Is.EqualTo("https"));
                Assert.That(consentUri.Host, Is.EqualTo($"{hobbit}"));
                Assert.That(consentUri.AbsolutePath, Is.EqualTo("/owner/appreg"));

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
                // The frontend consent page (/owner/youauth/authorize) shows a form
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
                Assert.That(redirectUri.Host, Is.EqualTo($"{hobbit}"));
                Assert.That(redirectUri.AbsolutePath, Is.EqualTo(finalRedirectUri.AbsolutePath));

                var qs = HttpUtility.ParseQueryString(redirectUri.Query);
                Assert.That(qs["code"], Is.Not.Null.And.Not.Empty);
            }
        }

        //

        [Test]
        public void c2_AlwaysGetConsentWhenClientTypeIsApp()
        {
            // An app always requires consent from the user.
            Assert.Fail("SEB:TODO implement me, lazy hooman!");
        }

        [Test]
        public void c2_GetConsentWhenClientTypeIsDomainAndConsentNotAlreadyGiven()
        {
            // Consent will be necessitated for any domain unless Sam has previously granted approval for automatic
            // consent on that specific domain.
            // If permission_request is set, and is requesting permissions not already granted,
            // then a consent is always required.
            Assert.Fail("SEB:TODO implement me, lazy hooman!");
        }

        //

        [Test]
        public void c3_IgnoreConsentWhenClientTypeIsDomainAndConsentIsAlreadyGiven()
        {
            // Consent will be necessitated for any domain unless Sam has previously granted approval for automatic
            // consent on that specific domain.
            // If permission_request is set, and is requesting permissions not already granted,
            // then a consent is always required.
            Assert.Fail("SEB:TODO implement me, lazy hooman!");
        }

        //

        [Test]
        public void c4_GetConsentWhenClientTypeIsDomainAndConsentWasPreviouslyGivenButPermissionRequestHasChanged()
        {
            // Consent will be necessitated for any domain unless Sam has previously granted approval for automatic
            // consent on that specific domain.
            // If permission_request is set, and is requesting permissions not already granted,
            // then a consent is always required.
            Assert.Fail("SEB:TODO implement me, lazy hooman!");
        }

        //

        [Test]
        public async Task d1_domain_WithImplicitConsentItShouldExchangeAuthorizationCodeForToken()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, ownerSharedSecret) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            //
            // [010] Generate code verifier
            //
            var codeVerifier = Guid.NewGuid().ToString();
            var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();

            //
            // [030] Request authorization code
            Uri returnUrl;

            const string thirdParty = "frodo.dotyou.cloud";
            var finalRedirectUri = new Uri($"https://{thirdParty}/foo/code/callback");
            
            //
            // [030] Request authorization code
            //
            {
                var payload = new YouAuthAuthorizeRequest
                {
                    ClientId = thirdParty,
                    ClientType = ClientType.domain,
                    ClientInfo = "",
                    CodeChallenge = codeChallenge,
                    PermissionRequest = "",
                    RedirectUri = $"https://{thirdParty}/foo/code/callback"
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

                // ... /owner/youauth/authorize
                var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                var consentUri = new Uri(location);
                Assert.That(consentUri.Scheme, Is.EqualTo("https"));
                Assert.That(consentUri.Host, Is.EqualTo($"{hobbit}"));

                //TODO: not sure why the consent url needs to be authorize; check w/ SEB
                // Assert.That(consentUri.AbsolutePath, Is.EqualTo("/owner/youauth/authorize"));
                Assert.That(consentUri.AbsolutePath, Is.EqualTo("/owner/youauth/consent"));

                // ... ?returnUrl= ...
                var qs = YouAuthTestHelper.ParseQueryString(location);
                returnUrl = new Uri(qs["returnUrl"]);
                Assert.That(returnUrl.ToString(), Does.StartWith($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Authorize}"));
            }

            //
            // [050] Consent needed, give consent
            //
            Uri authorizeUri;
            {
                //
                // The frontend consent page (/owner/youauth/authorize) shows a form
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
            // [080] return auth code to client
            //
            string code;
            {
                var payload = new YouAuthAuthorizeRequest
                {
                    ClientId = thirdParty,
                    ClientType = ClientType.domain,
                    ClientInfo = "",
                    CodeChallenge = codeChallenge,
                    PermissionRequest = "",
                    RedirectUri = $"https://{thirdParty}/foo/code/callback"
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
                Assert.That(qs["code"], Is.Not.Null.And.Not.Empty);
                code = qs["code"]!;
            }

            //
            // [100] Exchange auth code for access token
            // [140] Return client access token to client
            //
            {
                var uri = new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Token}");
                var tokenRequest = new YouAuthTokenRequest
                {
                    Code = code,
                    CodeVerifier = codeVerifier,
                    TokenDeliveryOption = TokenDeliveryOption.cookie
                };
                var body = OdinSystemSerializer.Serialize(tokenRequest);

                var request = new HttpRequestMessage(HttpMethod.Post, uri.ToString())
                {
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                var response = await apiClient.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

                var content = await response.Content.ReadAsStringAsync();
                var token = OdinSystemSerializer.Deserialize<YouAuthTokenResponse>(content);

                Assert.That(token!.Base64SharedSecret, Is.Not.Null.And.Not.Empty);
            }
        }

        //

        [Test]
        public async Task d2_domain_WithImplicitConsentClientAccessTokenShouldBeDeliveredAsCookieWhenTokenDeliveryOptionIsCookie()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, ownerSharedSecret) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            var appId = Guid.NewGuid();
            var appRegistration = RegisterApp(hobbit, appId);
            await ConnectHobbits();

            //
            // [010] Generate code verifier
            //
            var codeVerifier = Guid.NewGuid().ToString();
            var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();

            const string thirdParty = "frodo.dotyou.cloud";
            var finalRedirectUri = new Uri($"https://{thirdParty}/foo/code/callback");

            //
            // [030] Request authorization code
            // [070] Create auth code
            // [080] return auth code to client
            //
            string code;
            {
                var payload = new YouAuthAuthorizeRequest
                {
                    ClientId = thirdParty,
                    ClientType = ClientType.domain,
                    ClientInfo = "",
                    CodeChallenge = codeChallenge,
                    PermissionRequest = "",
                    RedirectUri = $"https://{thirdParty}/foo/code/callback"
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
                Assert.That(qs["code"], Is.Not.Null.And.Not.Empty);
                code = qs["code"]!;
            }

            //
            // [100] Exchange auth code for access token
            // [140] Return client access token to client
            //
            {
                var uri = new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Token}");
                var tokenRequest = new YouAuthTokenRequest
                {
                    Code = code,
                    CodeVerifier = codeVerifier,
                    TokenDeliveryOption = TokenDeliveryOption.cookie
                };
                var body = OdinSystemSerializer.Serialize(tokenRequest);

                var request = new HttpRequestMessage(HttpMethod.Post, uri.ToString())
                {
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                var response = await apiClient.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

                var content = await response.Content.ReadAsStringAsync();
                var token = OdinSystemSerializer.Deserialize<YouAuthTokenResponse>(content);

                Assert.That(token!.Base64SharedSecret, Is.Not.Null.And.Not.Empty);
                Assert.That(token!.Base64ClientAuthToken, Is.Null.Or.Empty);

                var cookies = response.GetCookies();
                Assert.That(cookies.ContainsKey(YouAuthTestHelper.HomeCookieName), Is.True);
                var homeCookie = cookies[YouAuthTestHelper.HomeCookieName];
                Assert.That(homeCookie, Is.Not.Null.And.Not.Empty);
            }
        }

        //

        [Test]
        public async Task d3_domain_WithImplicitConsentClientAccessTokenShouldBeDeliveredAsJsonResponseWhenTokenDeliveryOptionIsJson()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, ownerSharedSecret) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            //
            // [010] Generate code verifier
            //
            var codeVerifier = Guid.NewGuid().ToString();
            var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();

            const string thirdParty = "frodo.dotyou.cloud";
            var finalRedirectUri = new Uri($"https://{thirdParty}/foo/code/callback");

            //
            // [030] Request authorization code
            // [070] Create auth code
            // [080] return auth code to client
            //
            string code;
            {
                var payload = new YouAuthAuthorizeRequest
                {
                    ClientId = thirdParty,
                    ClientType = ClientType.domain,
                    ClientInfo = "",
                    CodeChallenge = codeChallenge,
                    PermissionRequest = "",
                    RedirectUri = $"https://{thirdParty}/foo/code/callback"
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
                Assert.That(qs["code"], Is.Not.Null.And.Not.Empty);
                code = qs["code"]!;
            }

            //
            // [100] Exchange auth code for access token
            // [140] Return client access token to client
            //
            {
                var uri = new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Token}");
                var tokenRequest = new YouAuthTokenRequest
                {
                    Code = code,
                    CodeVerifier = codeVerifier,
                    TokenDeliveryOption = TokenDeliveryOption.json
                };
                var body = OdinSystemSerializer.Serialize(tokenRequest);

                var request = new HttpRequestMessage(HttpMethod.Post, uri.ToString())
                {
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                var response = await apiClient.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

                var content = await response.Content.ReadAsStringAsync();
                var token = OdinSystemSerializer.Deserialize<YouAuthTokenResponse>(content);

                Assert.That(token!.Base64SharedSecret, Is.Not.Null.And.Not.Empty);
                Assert.That(token!.Base64ClientAuthToken, Is.Not.Null.And.Not.Empty);

                var cookies = response.GetCookies();
                Assert.That(cookies.ContainsKey(YouAuthTestHelper.HomeCookieName), Is.False);
            }
        }

        [Test]
        public async Task d4_domain_WithExplicitConsentClientAccessTokenShouldBeDeliveredAsCookieWhenTokenDeliveryOptionIsCookie()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, ownerSharedSecret) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            //
            // [010] Generate code verifier
            //
            var codeVerifier = Guid.NewGuid().ToString();
            var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();

            Uri returnUrl;
            const string thirdParty = "frodo.dotyou.cloud";
            var finalRedirectUri = new Uri($"https://{thirdParty}/foo/code/callback");

            //
            // [030] Request authorization code
            //
            {
                var payload = new YouAuthAuthorizeRequest
                {
                    ClientId = thirdParty,
                    ClientType = ClientType.domain,
                    ClientInfo = "",
                    CodeChallenge = codeChallenge,
                    PermissionRequest = "",
                    RedirectUri = $"https://{thirdParty}/foo/code/callback"
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

                // ... /owner/youauth/authorize
                var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                var consentUri = new Uri(location);
                Assert.That(consentUri.Scheme, Is.EqualTo("https"));
                Assert.That(consentUri.Host, Is.EqualTo($"{hobbit}"));
                Assert.That(consentUri.AbsolutePath, Is.EqualTo("/owner/consent"));

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
                // The frontend consent page (/owner/youauth/authorize) shows a form
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
            string code;
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
                Assert.That(qs["code"], Is.Not.Null.And.Not.Empty);
                code = qs["code"]!;
            }

            //
            // [100] Exchange auth code for access token
            // [140] Return client access token to client
            //
            {
                var uri = new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Token}");
                var tokenRequest = new YouAuthTokenRequest
                {
                    Code = code,
                    CodeVerifier = codeVerifier,
                    TokenDeliveryOption = TokenDeliveryOption.cookie
                };
                var body = OdinSystemSerializer.Serialize(tokenRequest);

                var request = new HttpRequestMessage(HttpMethod.Post, uri.ToString())
                {
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                var response = await apiClient.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

                var content = await response.Content.ReadAsStringAsync();
                var token = OdinSystemSerializer.Deserialize<YouAuthTokenResponse>(content);

                Assert.That(token!.Base64SharedSecret, Is.Not.Null.And.Not.Empty);
                Assert.That(token!.Base64ClientAuthToken, Is.Null.Or.Empty);

                var cookies = response.GetCookies();
                Assert.That(cookies.ContainsKey(YouAuthTestHelper.HomeCookieName), Is.True);
                var homeCookie = cookies[YouAuthTestHelper.HomeCookieName];
                Assert.That(homeCookie, Is.Not.Null.And.Not.Empty);
            }
        }

        //

        [Test]
        public async Task d5_domain_WithExplicitConsentClientAccessTokenShouldBeDeliveredAsJsonResponseWhenTokenDeliveryOptionIsJson()
        {
            const string hobbit = "sam.dotyou.cloud";
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, ownerSharedSecret) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);

            //
            // [010] Generate code verifier
            //
            var codeVerifier = Guid.NewGuid().ToString();
            var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();

            Uri returnUrl;
            const string thirdParty = "frodo.dotyou.cloud";
            var finalRedirectUri = new Uri($"https://{thirdParty}/foo/code/callback");

            //
            // [030] Request authorization code
            //
            {
                var payload = new YouAuthAuthorizeRequest
                {
                    ClientId = thirdParty,
                    ClientType = ClientType.domain,
                    ClientInfo = "",
                    CodeChallenge = codeChallenge,
                    PermissionRequest = "",
                    RedirectUri = $"https://{thirdParty}/foo/code/callback"
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

                // ... /owner/youauth/authorize
                var location = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                var consentUri = new Uri(location);
                Assert.That(consentUri.Scheme, Is.EqualTo("https"));
                Assert.That(consentUri.Host, Is.EqualTo($"{hobbit}"));
                Assert.That(consentUri.AbsolutePath, Is.EqualTo("/owner/consent"));

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
                // The frontend consent page (/owner/youauth/authorize) shows a form
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
            string code;
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
                Assert.That(qs["code"], Is.Not.Null.And.Not.Empty);
                code = qs["code"]!;
            }

            //
            // [100] Exchange auth code for access token
            // [140] Return client access token to client
            //
            {
                var uri = new UriBuilder($"https://{hobbit}{OwnerApiPathConstants.YouAuthV1Token}");
                var tokenRequest = new YouAuthTokenRequest
                {
                    Code = code,
                    CodeVerifier = codeVerifier,
                    TokenDeliveryOption = TokenDeliveryOption.json
                };
                var body = OdinSystemSerializer.Serialize(tokenRequest);

                var request = new HttpRequestMessage(HttpMethod.Post, uri.ToString())
                {
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                var response = await apiClient.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

                var content = await response.Content.ReadAsStringAsync();
                var token = OdinSystemSerializer.Deserialize<YouAuthTokenResponse>(content);

                Assert.That(token!.Base64SharedSecret, Is.Not.Null.And.Not.Empty);
                Assert.That(token!.Base64ClientAuthToken, Is.Not.Null.And.Not.Empty);

                var cookies = response.GetCookies();
                Assert.That(cookies.ContainsKey(YouAuthTestHelper.HomeCookieName), Is.False);
            }
        }
    }
}