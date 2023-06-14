using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Odin.Hosting.Controllers.Anonymous;
using Odin.Hosting.Controllers.OwnerToken.Auth;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Org.BouncyCastle.Utilities.Encoders;

//
// OWNER AUTHENTICATION:
//   COOKIE 'DY0810':
//     - Half-key (byte array): Half of the zero-knowledge key to "access" identity's resources on the server.
//       The half-key is stored in the ```DY0810``` by the authentication controller.
//     - Session ID (uuid)
    
//
//   Response when cookie is created:
//     Shared secret: the shared key for doing symmetric encryption client<->server (on top of TLS).
//     It is generated as part of authentication and is returned to the client by the authentication controller.
//     
// HOME AUTHENTICATION:
//
//    COOKIE 'XT32':    
// 
//   

//
// 
//   
//


namespace Odin.Hosting.Tests.YouAuthApi
{
    public class YouAuthIntegrationTests
    {
        private const string Password = "EnSøienØ";
        private const string OwnerCookieName = "DY0810";
            private const string HomeCookieName = "XT32";
        
        private WebScaffold _scaffold;
        private readonly JsonSerializerOptions _serializerOptions;

        public YouAuthIntegrationTests()
        {
            _serializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };
        }

        [SetUp]
        public void Init()
        {
            var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        //

        [TearDown]
        public void Cleanup()
        {
            _scaffold.RunAfterAnyTests();
        }

        //

        [Test, Combinatorial]
        public async Task VisitingHobbitCanAccessHomeOnVisitedHobbit(
            // [Values("frodo.dotyou.cloud")] string visitor, 
            // [Values("frodo.dotyou.cloud")] string host
            [Values("sam.dotyou.cloud")] string visitingHobbit, 
            [Values("frodo.dotyou.cloud")] string visitedHobbit
            )
        {
            //
            // `visitingHobbit` logs on to `visitedHobbit`s home
            //
            
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            
            // Prerequisite A:
            // Make sure we cannot access  home resource without valid home cookie
            {
                var response = await apiClient.GetAsync($"https://{visitedHobbit}/api/youauth/v1/auth/ping");
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            }
            
            //
            // Browser:
            // HOME "login" button click at frodo's home redirects to:
            //   https://sam.dotyou.cloud/owner/login/youauth?returnUrl=https://frodo.dotyou.cloud/
            // This triggers OWNER login and in turn YOUAUTH flow below
            //
            
            // Step 1:
            // Check owner cookie (we don't send any).
            // Backend will return false and we move on to owner-authentication in step 2.
            //
            // https://sam.dotyou.cloud/api/owner/v1/authentication/verifyToken
            {
                var response = await apiClient.GetAsync($"https://{visitingHobbit}/api/owner/v1/authentication/verifyToken");
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                var json = await response.Content.ReadAsStringAsync();
                Assert.That(JsonSerializer.Deserialize<bool>(json, _serializerOptions), Is.False);
            }
            
            //
            // Browser:
            // If visitingHobbit != visitedHobbit and the hobbits are not already connected,
            // then browser shows approval page: By logging in you allow "frodo.dotyou.cloud" to verify your identity
            // and personalise your experience"
            //
            // SEB:TODO
            // This must be a backend redirect operation and home cookie must only be created if user approves
            //
            
            // Step 2
            // Get nonce for authentication
            //
            // https://sam.dotyou.cloud/api/owner/v1/authentication/nonce
            NonceData nonceData;
            {
                var response = await apiClient.GetAsync($"https://{visitingHobbit}/api/owner/v1/authentication/nonce");
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                var json = await response.Content.ReadAsStringAsync();
                nonceData = JsonSerializer.Deserialize<NonceData>(json, _serializerOptions);
                Assert.That(nonceData.Nonce64, Is.Not.Null.And.Not.Empty);
            }

            // Step 3:
            // Authenticate
            //
            // https://sam.dotyou.cloud/api/owner/v1/authentication
            string ownerCookie;
            {
                var passwordReply = PasswordDataManager.CalculatePasswordReply(Password, nonceData);
                var json = JsonSerializer.Serialize(passwordReply);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await apiClient.PostAsync($"https://{visitingHobbit}/api/owner/v1/authentication", content);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

                // Shared secret from response
                json = await response.Content.ReadAsStringAsync(); 
                var ownerAuthResult = JsonSerializer.Deserialize<OwnerAuthenticationResult>(json, _serializerOptions);
                Assert.That(ownerAuthResult.SharedSecret, Is.Not.Null.And.Not.Empty);
                
                // Owner cookie from response
                var cookies = GetResponseCookies(response);
                ownerCookie = cookies[OwnerCookieName];
                Assert.That(ownerCookie, Is.Not.Null.And.Not.Empty);
            }

            //
            // Browser:
            // Now we have owner cookie and shared secret, we can start the YOUAUTH flow
            // Browser redirects, using window.location, to:
            //   https://sam.dotyou.cloud/owner/login/youauth?returnUrl=https://frodo.dotyou.cloud/
            //
            
            // Step 4:
            // Check owner cookie again (this time we have it, so send it)
            //
            // https://sam.dotyou.cloud/api/owner/v1/authentication/verifyToken
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"https://{visitingHobbit}/api/owner/v1/authentication/verifyToken")
                {
                    Headers = { { "Cookie", new Cookie(OwnerCookieName, ownerCookie).ToString() } }
                };
                var response = await apiClient.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                var json = await response.Content.ReadAsStringAsync();
                Assert.That(JsonSerializer.Deserialize<bool>(json, _serializerOptions), Is.True);
            }
            
            //
            // YOUAUTH YOUAUTH YOUAUTH YOUAUTH
            //
            // Frodo now has a verified session on frodo/owner via the owner cookie.
            // Start the YOUAUTH flow!
            //
            
            // Step 5:
            // Create token flow authorization code
            //
            // https://sam.dotyou.cloud/api/owner/v1/youauth/create-token-flow?returnUrl=https://frodo.dotyou.cloud/?identity=sam.dotyou.cloud
            string validateAuthorizationCodeUri;            
            {
                var returnUrl = WebUtility.UrlEncode($"https://{visitedHobbit}/?identity=frodo.dotyou.cloud");
                var uri = $"https://{visitingHobbit}/api/owner/v1/youauth/create-token-flow?returnUrl={returnUrl}";
                var request = new HttpRequestMessage(HttpMethod.Get, uri)
                {
                    Headers = { { "Cookie", new Cookie(OwnerCookieName, ownerCookie).ToString() } }
                };
                var response = await apiClient.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                validateAuthorizationCodeUri = GetHeaderValue(response, "Location");
                Assert.That(validateAuthorizationCodeUri, Does.StartWith($"https://api.{visitedHobbit}{YouAuthApiPathConstants.ValidateAuthorizationCodeRequestPath}"));
            }
            
            // Step 6:
            // Begin token flow using authorization code.
            // This step will trigger the backend `visitedHobbit` to `visitingHobbit` back channel call to validate the code from step 5.
            //
            // If validation succeeds the home cookie and shared secret are created.
            // Client is the redirected to an api "bridge" endpoint with the shared secret.
            // The brige endpoint will in turn redirect the client to the pseudo finalize-endpoint, 
            //   from which the client can can cherrypick the shared secret from the url.
            //
            // The bridge information is put in place so that the shared secret will end up at the apex domain,
            //   rather than the api domain, so it will be stored in local storage where it is actually needed.
            //
            // https://frodo.dotyou.cloud/api/youauth/v1/auth/validate-ac-req?ac=<auth-code>&subject=sam.dotyou.cloud&returnUrl=https://frodo.dotyou.cloud/?identity=sam.dotyou.cloud
            string finalizeBridgeUri;
            string homeCookie;
            string ss64;
            string finalizeReturnUrl;
            {
                var response = await apiClient.GetAsync(validateAuthorizationCodeUri);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                finalizeBridgeUri = GetHeaderValue(response, "Location");
                Assert.That(finalizeBridgeUri, Does.StartWith($"https://api.{visitedHobbit}{YouAuthApiPathConstants.FinalizeBridgeRequestRequestPath}"));
                var cookies = GetResponseCookies(response);
                homeCookie = cookies[HomeCookieName];
                Assert.That(homeCookie, Is.Not.Null.And.Not.Empty);

                var uri = new Uri(finalizeBridgeUri);
                var queryParameters = HttpUtility.ParseQueryString(uri.Query);
                ss64 = queryParameters["ss64"];
                Assert.That(ss64, Is.Not.Null.And.Not.Empty);
                finalizeReturnUrl = queryParameters["returnUrl"];
                Assert.That(finalizeReturnUrl, Is.Not.Null.And.Not.Empty);
            }

            // Step 8:
            // Finalize pseudo endpoint (see above explation)
            // https://frodo.dotyou.cloud/home/youauth/finalize?ss64=<shared-secret>&returnUrl=https://frodo.dotyou.cloud/?identity=sam.dotyou.cloud
            {
                var response = await apiClient.GetAsync(finalizeBridgeUri);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                var finalizeUri = GetHeaderValue(response, "Location");
                Assert.That(finalizeUri, Does.StartWith($"https://{visitedHobbit}/home/youauth/finalize"));
                var cookies = GetResponseCookies(response);
                Assert.That(cookies.ContainsKey(HomeCookieName), Is.False);

                var uri = new Uri(finalizeBridgeUri);
                var queryParameters = HttpUtility.ParseQueryString(uri.Query);
                var ss64Copy = queryParameters["ss64"];
                Assert.That(ss64Copy, Is.EqualTo(ss64));
                var finalizeReturnUrlCopy = queryParameters["returnUrl"];
                Assert.That(finalizeReturnUrlCopy, Is.EqualTo(finalizeReturnUrl));
            }
            
            //
            // YouAuth flow done
            //
            
            // Postrequisite A
            // Access resource using home cookie and shared secret
            {
                var uri = UriWithEncryptedQueryString($"https://{visitedHobbit}/api/youauth/v1/auth/ping?text=helloworld", ss64);
                var request = new HttpRequestMessage(HttpMethod.Get, uri)
                {
                    Headers = { { "Cookie", new Cookie(HomeCookieName, homeCookie).ToString() } }
                };
                var response = await apiClient.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                var text = await DecryptContent<string>(response, ss64);
                Assert.That(text, Is.EqualTo($"ping from {visitedHobbit}: helloworld"));
            }
        }
        
        //

        private string GetHeaderValue(HttpResponseMessage response, string headerName)
        {
            if (response.Headers.TryGetValues(headerName, out var values))
            {
                return values.FirstOrDefault();
            }
            return default;
        }
        
        //

        private Dictionary<string, string> GetResponseCookies(HttpResponseMessage response)
        {
            var result = new Dictionary<string, string>();
            
            if (response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
            {
                foreach (var cookieHeader in cookieHeaders)
                {
                    var name = cookieHeader.Split(';')[0].Split('=')[0];
                    var value = cookieHeader.Split(';')[0].Split('=')[1];
                    result[name] = value;
                }
            }

            return result;
        }
        
        //

        private string UriWithEncryptedQueryString(string uri, string sharedSecretBase64)
        {
            var queryIndex = uri.IndexOf('?');
            if (queryIndex == -1 || queryIndex == uri.Length - 1)
            {
                return uri;
            }
            
            var path = uri[..queryIndex];
            var query = uri[(queryIndex + 1)..];
            
            var keyBytes = Base64.Decode(sharedSecretBase64);
            var key = new SensitiveByteArray(keyBytes);

            var iv = ByteArrayUtil.GetRndByteArray(16);
            var encryptedBytes = AesCbc.Encrypt(query.ToUtf8ByteArray(), ref key, iv);

            var payload = new SharedSecretEncryptedPayload()
            {
                Iv = iv,
                Data = encryptedBytes.ToBase64()
            };

            uri = $"{path}?ss={HttpUtility.UrlEncode(OdinSystemSerializer.Serialize(payload))}";

            return uri;
        }
        
        //

        private async Task<T> DecryptContent<T>(HttpResponseMessage response, string sharedSecretBase64)
        {
            var cipherJson = await response.Content.ReadAsStringAsync();
            var payload = OdinSystemSerializer.Deserialize<SharedSecretEncryptedPayload>(cipherJson);
            
            var keyBytes = Base64.Decode(sharedSecretBase64);
            var key = new SensitiveByteArray(keyBytes);
            
            var plainJson = AesCbc.Decrypt(Convert.FromBase64String(payload.Data), ref key, payload.Iv);
            
            var result = JsonSerializer.Deserialize<T>(plainJson, _serializerOptions);

            return result;
        }
    }
}


