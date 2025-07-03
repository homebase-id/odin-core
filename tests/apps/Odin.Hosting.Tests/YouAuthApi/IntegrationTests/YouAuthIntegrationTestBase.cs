using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using NUnit.Framework;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Hosting.Controllers.OwnerToken.Auth;
using Odin.Hosting.Controllers.OwnerToken.YouAuth;
using Odin.Core.Cryptography.Crypto;

#nullable enable
namespace Odin.Hosting.Tests.YouAuthApi.IntegrationTests;

public abstract class YouAuthIntegrationTestBase
{
    protected WebScaffold Scaffold = null!;
    protected IServiceProvider Services => Scaffold.Services;

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
    protected async Task<(string, string)> AuthenticateOwnerReturnOwnerCookieAndSharedSecret(string identity)
    {
        var apiClient = WebScaffold.HttpClientFactory.CreateClient($"{identity}:{WebScaffold.HttpsPort}");

        // Step 1:
        // Check owner cookie (we don't send any).
        // Backend will return false and we move on to owner-authentication in step 2.
        //
        // https://sam.dotyou.cloud/api/owner/v1/authentication/verifyToken
        {
            var response = await apiClient.GetAsync($"https://{identity}:{WebScaffold.HttpsPort}/api/owner/v1/authentication/verifyToken");
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
            var response = await apiClient.GetAsync($"https://{identity}:{WebScaffold.HttpsPort}/api/owner/v1/authentication/nonce");
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
            var response = await apiClient.PostAsync($"https://{identity}:{WebScaffold.HttpsPort}/api/owner/v1/authentication", content);
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
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://{identity}:{WebScaffold.HttpsPort}/api/owner/v1/authentication/verifyToken")
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

    protected async Task<RedactedAppRegistration> RegisterApp(
        string identity,
        Guid appId,
        Guid driveAlias,
        Guid driveType)
    {
        var ownerClient = Scaffold.CreateOwnerApiClient(TestIdentities.All[identity]);

        var drive = new TargetDrive
        {
            Alias = driveAlias,
            Type = driveType
        };

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

        var appRegistration = await ownerClient.Apps.RegisterApp(appId, appPermissionsGrant);

        return appRegistration;
    }

    //

    protected async Task ConnectHobbits()
    {
        var targetDrive = TargetDrive.NewTargetDrive();
        await Scaffold.Scenarios.CreateConnectedHobbits(targetDrive);
    }

    //

    protected async Task DisconnectHobbits(TestIdentity a, TestIdentity b)
    {
        var ownerClient = Scaffold.CreateOwnerApiClient(a);
        if (await ownerClient.Network.IsConnected(b))
        {
            await ownerClient.Network.DisconnectFrom(b);
        }

        ownerClient = Scaffold.CreateOwnerApiClient(b);
        if (await ownerClient.Network.IsConnected(a))
        {
            await ownerClient.Network.DisconnectFrom(a);
        }
    }

    //

    protected YouAuthAppParameters GetAppPhotosParams()
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
            Return = "backend-will-decide",
        };

        return appParams;
    }

    //

    protected YouAuthAppParameters GetAppParams(Guid appId, Guid driveAlias, Guid driveType)
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

    protected async Task<QueryBatchResponse> QueryBatch(
        string domain,
        string clientAuthToken,
        string sharedSecret,
        string driveAlias,
        string driveType)
    {
        var catCookie = new Cookie("BX0900", clientAuthToken);
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("X-ODIN-FILE-SYSTEM-TYPE", "Standard");
        client.DefaultRequestHeaders.Add("Cookie", catCookie.ToString());

        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["maxRecords"] = "1000";
        qs["includeMetadataHeader"] = "true";
        qs["alias"] = driveAlias;
        qs["type"] = driveType;
        qs["fileState"] = "1";

        var url = $"https://{domain}:{WebScaffold.HttpsPort}/api/apps/v1/drive/query/batch?{qs}";

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

    //



}