using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Data;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Hosting.Controllers.OwnerToken.Auth;

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
// APP HEADER: BX0900
//
// 
//   
//

#nullable enable
namespace Odin.Hosting.Tests.YouAuthApi.IntegrationTests;

public abstract class YouAuthIntegrationTestBase
{
    protected WebScaffold Scaffold = null!;

    [SetUp]
    public void Init()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        Scaffold = new WebScaffold(folder);
        Scaffold.RunBeforeAnyTests();
    }

    //

    [TearDown]
    public void Cleanup()
    {
        Scaffold.RunAfterAnyTests();
    }

    // 

    // Authenticate and return owner cookie and shared secret
    protected async Task<(string, string)> AuthenticateOwnerReturnOwnerCookieAndSharedSecret(string identity, Guid? appId = null)
    {
        var apiClient = WebScaffold.CreateDefaultHttpClient();


        var ownerClient = Scaffold.CreateOwnerApiClient(TestIdentities.All[identity]);

        var drive = TargetDrive.NewTargetDrive();
        var _ = await ownerClient.Drive.CreateDrive(drive, "Test Drive", "Test Drive", false, false, false);

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

        var appRegistration = await ownerClient.Apps.RegisterApp(appId.GetValueOrDefault(Guid.NewGuid()), appPermissionsGrant);


        // Step 1:
        // Check owner cookie (we don't send any).
        // Backend will return false and we move on to owner-authentication in step 2.
        //
        // https://sam.dotyou.cloud/api/owner/v1/authentication/verifyToken
        {
            var response = await apiClient.GetAsync($"https://{identity}/api/owner/v1/authentication/verifyToken");
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
            var response = await apiClient.GetAsync($"https://{identity}/api/owner/v1/authentication/nonce");
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
            var passwordReply = PasswordDataManager.CalculatePasswordReply(YouAuthTestHelper.Password, nonceData);
            var json = YouAuthTestHelper.Serialize(passwordReply);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await apiClient.PostAsync($"https://{identity}/api/owner/v1/authentication", content);
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
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://{identity}/api/owner/v1/authentication/verifyToken")
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
}