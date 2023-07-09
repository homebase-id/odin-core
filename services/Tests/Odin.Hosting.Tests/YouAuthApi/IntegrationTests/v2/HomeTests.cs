#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using NUnit.Framework;
using Odin.Hosting.Controllers.Anonymous;

namespace Odin.Hosting.Tests.YouAuthApi.IntegrationTests.v2
{
    public class HomeTests : YouAuthIntegrationTestBase
    {

        //

        [Test, Combinatorial]
        public async Task VisitingHobbitCanAccessHomeOnVisitedHobbit(
            [Values("frodo.dotyou.cloud", "sam.dotyou.cloud")] string visitingHobbit, 
            [Values("frodo.dotyou.cloud", "sam.dotyou.cloud")] string visitedHobbit)
        {
            //
            // `visitingHobbit` logs on to `visitedHobbit`s home
            //
            
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            
            #region Prerequisites:
            
            // Make sure we cannot access home resource without valid home cookie
            {
                var response = await apiClient.GetAsync($"https://{visitedHobbit}/api/youauth/v1/auth/ping");
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            }
            
            #endregion
            
          
            /////////////////////////////////////////////////////////////////////////
            //
            // Sam types his Odin ID samwisegamgee.me
            //   and clicks login on Frodo's identity host
            //
            // home:  https://frodo.dotyou.cloud/home
            //
            /////////////////////////////////////////////////////////////////////////
            
            
            //
            // [010]
            //
            // Frodo's identity host redirects Sam's browser to his own host for owner-authentication
            //   and adds it's public key and returnURL
            //
            // Redirect:
            //   https://sam.dotyou.cloud/owner/login/youauth?returnUrl=https://frodo.dotyou.cloud/
            //
            // Parameters:
            //   rsa=base64string SEB:TODO
            //   returnUrl=https://frodo.dotyou.cloud/
            //
            
            //
            // [012]
            //
            // If Sam is not logged in, redirect Sam's browser to login sequence diagram D0200
            // Redirect: https://identity/owner/login
            // Parameters:
            //   returnUrl=redirect back here when logged in.
            //
            // SEB:NOTE this is currently handled by the front-end by querying the backend if Sam is logged
            // on to /owner. If Sam is not logged on, the authentication page is shown.
            //
            
            var (ownerCookie, ownerSharedSecret) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(visitingHobbit);

            //
            // Browser:
            // Now we have owner cookie and shared secret, we can start the YOUAUTH flow
            // Browser redirects, using window.location, to:
            //   https://sam.dotyou.cloud/owner/login/youauth?returnUrl=https://frodo.dotyou.cloud/
            //
            
            //
            // [014]
            //
            // If visitingHobbit != visitedHobbit and the hobbits are not already connected,
            // then browser shows approval page: By logging in you allow "frodo.dotyou.cloud" to verify your identity
            // and personalise your experience"
            //
            // NOTE: This is currently controlled exclusively by front-end
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
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } }
                };
                var response = await apiClient.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                validateAuthorizationCodeUri = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
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
                finalizeBridgeUri = response.GetHeaderValue("Location") ?? throw new Exception("missing location");
                Assert.That(finalizeBridgeUri, Does.StartWith($"https://api.{visitedHobbit}{YouAuthApiPathConstants.FinalizeBridgeRequestRequestPath}"));
                var cookies = response.GetCookies();
                homeCookie = cookies[YouAuthTestHelper.HomeCookieName];
                Assert.That(homeCookie, Is.Not.Null.And.Not.Empty);

                var uri = new Uri(finalizeBridgeUri);
                var queryParameters = HttpUtility.ParseQueryString(uri.Query);
                ss64 = queryParameters["ss64"] ?? throw new Exception("missing ss64");
                Assert.That(ss64, Is.Not.Null.And.Not.Empty);
                finalizeReturnUrl = queryParameters["returnUrl"] ?? throw new Exception("missing returnUrl");
                Assert.That(finalizeReturnUrl, Is.Not.Null.And.Not.Empty);
            }

            // Step 8:
            // Finalize pseudo endpoint (see above explation)
            // https://frodo.dotyou.cloud/home/youauth/finalize?ss64=<shared-secret>&returnUrl=https://frodo.dotyou.cloud/?identity=sam.dotyou.cloud
            {
                var response = await apiClient.GetAsync(finalizeBridgeUri);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                var finalizeUri = response.GetHeaderValue("Location");
                Assert.That(finalizeUri, Does.StartWith($"https://{visitedHobbit}/home/youauth/finalize"));
                var cookies = response.GetCookies();
                Assert.That(cookies.ContainsKey(YouAuthTestHelper.HomeCookieName), Is.False);

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
                var uri = YouAuthTestHelper.UriWithEncryptedQueryString($"https://{visitedHobbit}/api/youauth/v1/auth/ping?text=helloworld", ss64);
                var request = new HttpRequestMessage(HttpMethod.Get, uri)
                {
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.HomeCookieName, homeCookie).ToString() } }
                };
                var response = await apiClient.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                var text = await YouAuthTestHelper.DecryptContent<string>(response, ss64);
                Assert.That(text, Is.EqualTo($"ping from {visitedHobbit}: helloworld"));
            }
        }
        
        //
        
    }
}


