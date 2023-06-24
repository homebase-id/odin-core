using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Services.Authorization.Apps;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.Management;
using Odin.Core.Services.Transit;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace Odin.Hosting.Tests.YouAuthApi.IntegrationTests.Current;

#nullable enable
public class AppTests : YouAuthIntegrationTestBase
{
    [Test]
    public async Task FrodoLogsInToPhotoAppAndAccesesAPhoto()
    {
        var hobbit = "frodo.dotyou.cloud";
        
        var apiClient = WebScaffold.CreateDefaultHttpClient();
        
        // Prerequisite A:
        // Make sure we cannot access photos without a valid token
        {
            // SEB:NOTE YouAuthTestHelper.GenerateQueryString() doesn't work here because FileType is an array.
            var queryString =
                $"alias={YouAuthTestHelper.PhotosDriveAlias}" + "&" +
                $"fileType=400" + "&" +
                $"maxRecords=1000" + "&" +
                $"type={YouAuthTestHelper.PhotosDriveType}" + "&" +
                $"includeMetadataHeader=true";
            
            var uri = $"https://{hobbit}/api/apps/v1/drive/query/batch?{queryString}";            
            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Headers =
                {
                    { "X-ODIN-FILE-SYSTEM-TYPE", "Standard" }
                }
            };
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }
        
        //
        // Browser:
        // PHOTOS "login" button on photo-app (login-box iframe) redirects to:
        // https://frodo.dotyou.cloud/owner/appreg
        //   ?n=Odin - Photos
        //   &o=dev.dotyou.cloud:3005
        //   &appId=32f0bdbf-017f-4fc0-8004-2d4631182d1e
        //   &fn=Chrome | macOS
        //   &return=https://dev.dotyou.cloud:3005/auth/finalize?returnUrl=/&
        //   &d=[{"a":"6483b7b1f71bd43eb6896c86148668cc","t":"2af68fe72fb84896f39f97c59d60813a","n":"Photo Library","d":"Place for your memories","p":3}]
        //   &pk=MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA5ElQ%2BxS%2F081ivWfqOmqklJqfIQOvVq%2B5JAmoppPTrZ4A8Yiv3xytiDzfQejjHSKnlGj2bC699aV592D2TKFQ86Yzdf2WRYOxwI0Ri%2FvievCediuFugmKAG6dsuEThkFp5UdNeKYJnioOwoTld5PhFXPv%2BLJAC4gxyaLeVNCGnX0HCDN%2F50%2Bkbgy9IfZJRsiJqf3RSb09ds47ot4H6WK0a4eOlP6fbCZaYw2ZYs0zN9PXDa5%2FHD1pAYFe7SJYfua2cMbwpbisEC2PJBm0gG92SDQ6%2FYzxChNe2CrCY5eNjQynaScuVXvIOJMtNI68Sks9E1ChpNRfglv2zg2fIhi5QwIDAQAB
        //
        
        // Login at owner:
        var (ownerCookie, ownerSharedSecret) = await AuthenticateOwnerReturnOwnerCookieAndSharedSecret(hobbit);
        
        //
        // Get drive by type (PhotosDriveType)
        // Drive should not exist yet.
        //
        {
            var queryString = YouAuthTestHelper.GenerateQueryString( 
                new GetDrivesByTypeRequest
                {
                    DriveType = YouAuthTestHelper.PhotosDriveType,
                    PageNumber = 1,
                    PageSize = 25
                });

            var uri = YouAuthTestHelper.UriWithEncryptedQueryString(
                $"https://{hobbit}/api/owner/v1/drive/mgmt/type?{queryString}", ownerSharedSecret);            

            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } }
            };
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var driveData = await YouAuthTestHelper.DecryptContent<PagedResult<OwnerClientDriveData>>(response, ownerSharedSecret);
            Assert.That(driveData.TotalPages, Is.EqualTo(1));
            Assert.That(driveData.Results, Is.Empty);
        }
        
        //
        // Get list of registered clients (i.e. the combination of app and client program that Frodo has 
        // consented to accessing his photos).
        // At this point we havent given consent yet, so the client list will be empty.
        //
        {
            var uri = $"https://{hobbit}/api/owner/v1/appmanagement/clients";
            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } }
            };
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var clientList = await YouAuthTestHelper.DecryptContent<List<RegisteredAppClientResponse>>(response, ownerSharedSecret);
            Assert.That(clientList, Is.Empty);
        }
        
        //
        // Get information about a registered app.
        // At this point we havent given consent yet, so nothing is returned.
        //
        {
            var uri = $"https://{hobbit}/api/owner/v1/appmanagement/app";
            var body = new GetAppRequest { AppId = YouAuthTestHelper.PhotosAppId };
            var content = YouAuthTestHelper.EncryptContent(body, ownerSharedSecret);
            var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                Content = content
            };
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            var json = await response.Content.ReadAsStringAsync();
            Assert.That(json, Is.Empty);
        }
        
        //
        // show CONSENT
        // "By allowing "dev.dotyou.cloud:3005" to login you give it access to your Odin - Photos app"
        //
        // Click "login" button on CONSENT page:
        //
        
        //
        // Create a new drive for the photos 
        //
        {
            var uri = $"https://{hobbit}/api/owner/v1/drive/mgmt/create";
            var body = new CreateDriveRequest
            {
                AllowAnonymousReads = false,
                AllowSubscriptions = false,
                Metadata = "Place for your memories",
                Name = "Photo Library",
                OwnerOnly = false,
                TargetDrive = new TargetDrive
                {
                    Alias = YouAuthTestHelper.PhotosDriveAlias,
                    Type = YouAuthTestHelper.PhotosDriveType
                }
            };
            var content = YouAuthTestHelper.EncryptContent(body, ownerSharedSecret);
            var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                Content = content
            };
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var success = await YouAuthTestHelper.DecryptContent<bool>(response, ownerSharedSecret);
            Assert.That(success, Is.True);
        }
        
        //
        // Get drive by type (PhotosDriveType)
        // Verify that drive now exists.
        //
        {
            var queryString = YouAuthTestHelper.GenerateQueryString( 
                new GetDrivesByTypeRequest
                {
                    DriveType = YouAuthTestHelper.PhotosDriveType,
                    PageNumber = 1,
                    PageSize = 25
                });

            var uri = YouAuthTestHelper.UriWithEncryptedQueryString(
                $"https://{hobbit}/api/owner/v1/drive/mgmt/type?{queryString}", ownerSharedSecret);            

            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } }
            };
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var driveData = await YouAuthTestHelper.DecryptContent<PagedResult<OwnerClientDriveData>>(response, ownerSharedSecret);
            Assert.That(driveData.TotalPages, Is.EqualTo(1));
            Assert.That(driveData.Results.Count, Is.EqualTo(1));
            Assert.That(driveData.Results[0].TargetDriveInfo.Type, Is.EqualTo(new GuidId(YouAuthTestHelper.PhotosDriveType)));
        }
        
        //
        // Register App
        //
        {
            var uri = $"https://{hobbit}/api/owner/v1/appmanagement/register/app";
            var body = new AppRegistrationRequest
            {
                AppId = YouAuthTestHelper.PhotosAppId,
                Name = "Odin - Photos",
                AuthorizedCircles = new List<Guid>(),
                CircleMemberPermissionGrant = new PermissionSetGrantRequest(),
                CorsHostName = "dev.dotyou.cloud:3005",
                PermissionSet = null,
                Drives = new List<DriveGrantRequest>()
                {
                    new() 
                    {
                        PermissionedDrive = new PermissionedDrive
                        {
                            Drive = new TargetDrive
                            {
                                Alias = YouAuthTestHelper.PhotosDriveAlias,
                                Type = YouAuthTestHelper.PhotosDriveType,
                            },
                            Permission = DrivePermission.Read | DrivePermission.Write 
                        }
                    }
                }
            };
            var content = YouAuthTestHelper.EncryptContent(body, ownerSharedSecret);
            var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                Content = content
            };
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var appRegistration = await YouAuthTestHelper.DecryptContent<RedactedAppRegistration>(response, ownerSharedSecret);
            Assert.That(appRegistration.AppId, Is.EqualTo(new GuidId(YouAuthTestHelper.PhotosAppId)));
        }
        
        //
        // Get information about a registered app.
        // Verify that app is now be registered
        //
        {
            var uri = $"https://{hobbit}/api/owner/v1/appmanagement/app";
            var body = new GetAppRequest { AppId = YouAuthTestHelper.PhotosAppId };
            var content = YouAuthTestHelper.EncryptContent(body, ownerSharedSecret);
            var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                Content = content
            };
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var appRegistration = await YouAuthTestHelper.DecryptContent<RedactedAppRegistration>(response, ownerSharedSecret);
            Assert.That(appRegistration.AppId, Is.EqualTo(new GuidId(YouAuthTestHelper.PhotosAppId)));
        }
        
        //
        // Register Client (e.g. browser)
        //
        string appAuthenticationTokenBase64;
        string appSharedSecretBase64;
        {
            var keys = YouAuthTestHelper.GenerateKeyPair();
            var publicKeyInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(keys.Public);
            var publicKeyDer = publicKeyInfo.GetDerEncoded();
            var base64PublicKeyDer = Convert.ToBase64String(publicKeyDer);
            
            var uri = $"https://{hobbit}/api/owner/v1/appmanagement/register/client";
            var body = new AppClientRegistrationRequest
            {
                AppId = YouAuthTestHelper.PhotosAppId,
                ClientFriendlyName = "Firefox | macOS",
                ClientPublicKey64 = base64PublicKeyDer
            };
            var content = YouAuthTestHelper.EncryptContent(body, ownerSharedSecret);
            var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Headers = { { "Cookie", new Cookie(YouAuthTestHelper.OwnerCookieName, ownerCookie).ToString() } },
                Content = content
            };
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var clientRegistration = await YouAuthTestHelper.DecryptContent<AppClientRegistrationResponse>(response, ownerSharedSecret);
            Assert.That(clientRegistration.Data.Length, Is.EqualTo(256));
            Assert.That(clientRegistration.EncryptionVersion, Is.EqualTo(1));
            
            var cipher = CipherUtilities.GetCipher("RSA/ECB/OAEPWithSHA256AndMGF1Padding");
            cipher.Init(false, keys.Private);
            var plainBytes = cipher.DoFinal(clientRegistration.Data);
            Assert.That(plainBytes.Length, Is.EqualTo(49));

            var appAuthenticationToken = plainBytes.Take(..33).ToArray();
            appAuthenticationTokenBase64 = Convert.ToBase64String(appAuthenticationToken);
            var appSharedSecret = plainBytes.Take(33..).ToArray();
            appSharedSecretBase64 = Convert.ToBase64String(appSharedSecret);
        }
        
        //
        // Verify that we're now allowed to query photo drive using the app authentication token and app shared
        // secret.
        // 
        {
            // SEB:NOTE YouAuthTestHelper.GenerateQueryString() doesn't work here because FileType is an array.
            var queryString =
                $"alias={YouAuthTestHelper.PhotosDriveAlias}" + "&" +
                $"fileType=400" + "&" +
                $"maxRecords=1000" + "&" +
                $"type={YouAuthTestHelper.PhotosDriveType}" + "&" +
                $"includeMetadataHeader=true";
            
            var uri = YouAuthTestHelper.UriWithEncryptedQueryString(
                $"https://{hobbit}/api/apps/v1/drive/query/batch?{queryString}", appSharedSecretBase64);            
            
            var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Headers =
                {
                    { YouAuthTestHelper.AppAuthTokenHeaderName, appAuthenticationTokenBase64 },
                    { "X-ODIN-FILE-SYSTEM-TYPE", "Standard" }
                }
            };
            var response = await apiClient.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var batchResponse = await YouAuthTestHelper.DecryptContent<QueryBatchResponse>(response, appSharedSecretBase64);
            Assert.That(batchResponse, Is.Not.Null);
        }
    }

}