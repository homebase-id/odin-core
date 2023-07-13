#nullable enable
using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http.Extensions;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Services.Authentication.YouAuth;
using Odin.Hosting.Controllers.Anonymous;

namespace Odin.Hosting.Tests.YouAuthApi.IntegrationTests.v2
{
    public class UnifiedTests : YouAuthIntegrationTestBase
    {

        //

        [Test, Combinatorial]
        public async Task AuthorizeAuthenticatedHobbitOnHome(
            [Values("sam.dotyou.cloud")] string visitingHobbit, 
            [Values("frodo.dotyou.cloud")] string visitedHobbit)
        {
            //
            // `visitingHobbit` logs on to `visitedHobbit`s home
            // `visitingHobbit` is already authenticated
            //
            
            var apiClient = WebScaffold.CreateDefaultHttpClient();
            var (ownerCookie, ownerSharedSecret) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(visitingHobbit);
            
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
            //   and clicks login 
            //
            /////////////////////////////////////////////////////////////////////////
            
            //
            // [005]
            //
            var codeVerifier = Guid.NewGuid().ToString();
            var codeChallenge = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier)).ToBase64();

            //
            // [010] Request authorization code
            //
            {
                //
                // Arrange
                //
                var clientId = Guid.NewGuid().ToString();
                var clientInfo = new YouAuthClientInfo
                {
                    Name = "My Awesome App",
                    Description = "Description of My Awesome App"
                }.ToJson();
                var scope = "identity:read identity:write";
                var redirectUri = "https://response-here-please.example.com";

                var uri = new UriBuilder($"https://{visitingHobbit}/api/owner/v1/youauth/authorize")
                {
                    Query = new QueryBuilder
                    {
                        { "client_id", clientId },
                        { "client_info", clientInfo },
                        { "scope", scope },
                        { "redirect_uri", redirectUri }
                    }.ToString()

                }.ToString();
                
                var request = new HttpRequestMessage(HttpMethod.Get, uri)
                {
                    Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } }
                };
                
                //
                // Act 
                //
                var response = await apiClient.SendAsync(request);
                
                //
                // Assert
                //
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                
                
            }
            

            
            //
            // YouAuth flow done
            //
            
            // Postrequisite A
            // Access resource using home cookie and shared secret
            {
                // var uri = YouAuthTestHelper.UriWithEncryptedQueryString($"https://{visitedHobbit}/api/youauth/v1/auth/ping?text=helloworld", ss64);
                // var request = new HttpRequestMessage(HttpMethod.Get, uri)
                // {
                //     Headers = { { "Cookie", new Cookie(YouAuthTestHelper.HomeCookieName, homeCookie).ToString() } }
                // };
                // var response = await apiClient.SendAsync(request);
                // Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                // var text = await YouAuthTestHelper.DecryptContent<string>(response, ss64);
                // Assert.That(text, Is.EqualTo($"ping from {visitedHobbit}: helloworld"));
            }
        }
        
        //
        
    }
}


