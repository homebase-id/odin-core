using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.WebUtilities;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Membership.Definition;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Drives.Management;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Controllers.OwnerToken.AppManagement;
using Youverse.Hosting.Controllers.OwnerToken.Drive;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.AppAPI.Utils;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;
using Youverse.Hosting.Tests.OwnerApi.Apps;
using Youverse.Hosting.Tests.OwnerApi.Authentication;
using Youverse.Hosting.Tests.OwnerApi.Circle;
using Youverse.Hosting.Tests.OwnerApi.Configuration;
using Youverse.Hosting.Tests.OwnerApi.Drive;
using Youverse.Hosting.Tests.OwnerApi.Drive.Management;

namespace Youverse.Hosting.Tests.OwnerApi.Utils
{
    public class OwnerApiTestUtils
    {
        private readonly string _password = "EnSøienØ";
        private readonly Dictionary<string, OwnerAuthTokenContext> _ownerLoginTokens = new(StringComparer.InvariantCultureIgnoreCase);

        private static bool ServerCertificateCustomValidation(HttpRequestMessage requestMessage, X509Certificate2 certificate, X509Chain chain, SslPolicyErrors sslErrors)
        {
            // It is possible to inspect the certificate provided by the server.
            Console.WriteLine($"Requested URI: {requestMessage.RequestUri}");
            Console.WriteLine($"Effective date: {certificate.GetEffectiveDateString()}");
            Console.WriteLine($"Exp date: {certificate.GetExpirationDateString()}");
            Console.WriteLine($"Issuer: {certificate.Issuer}");
            Console.WriteLine($"Subject: {certificate.Subject}");

            // Based on the custom logic it is possible to decide whether the client considers certificate valid or not
            Console.WriteLine($"Errors: {sslErrors}");

            if (chain == null)
            {
                Console.WriteLine("No chain...");
            }
            else
            {
                foreach (X509ChainElement element in chain.ChainElements)
                {
                    Console.WriteLine();
                    Console.WriteLine(element.Certificate.Subject);
                    Console.WriteLine(element.ChainElementStatus.Length);
                    foreach (X509ChainStatus status in element.ChainElementStatus)
                    {
                        Console.WriteLine($"Status:  {status.Status}: {status.StatusInformation}");
                    }
                }
            }

            return true;
        }


        public async Task ForceNewPassword(string identity, string password)
        {
            var handler = new HttpClientHandler();
            var jar = new CookieContainer();
            handler.CookieContainer = jar;
            handler.UseCookies = true;

            // handler.CheckCertificateRevocationList = false;
            handler.ServerCertificateCustomValidationCallback = ServerCertificateCustomValidation;

            using HttpClient authClient = new(handler);
            authClient.BaseAddress = new Uri($"https://{identity}");
            var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

            Console.WriteLine($"forcing new password on {authClient.BaseAddress}");

            var saltResponse = await svc.GenerateNewSalts();
            Assert.IsNotNull(saltResponse.Content, "failed to generate new salts");
            Assert.IsTrue(saltResponse.IsSuccessStatusCode, "failed to generate new salts");
            var clientSalts = saltResponse.Content;
            var saltyNonce = new NonceData(clientSalts.SaltPassword64, clientSalts.SaltKek64, clientSalts.PublicPem, clientSalts.CRC)
            {
                Nonce64 = clientSalts.Nonce64
            };
            var saltyReply = PasswordDataManager.CalculatePasswordReply(password, saltyNonce);

            //saltyReply.FirstRunToken = ???

            var newPasswordResponse = await svc.SetNewPassword(saltyReply);
            Assert.IsTrue(newPasswordResponse.IsSuccessStatusCode, "failed forcing a new password");
        }

        public async Task<(ClientAuthenticationToken cat, SensitiveByteArray sharedSecret)> LoginToOwnerConsole(string identity, string password)
        {
            var handler = new HttpClientHandler();
            var jar = new CookieContainer();
            handler.CookieContainer = jar;
            handler.UseCookies = true;

            using HttpClient authClient = new(handler);
            authClient.BaseAddress = new Uri($"https://{identity}");
            var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

            var uri = new Uri($"https://{identity}");

            Console.WriteLine($"authenticating to {uri}");
            var nonceResponse = await svc.GenerateNonce();
            Assert.IsTrue(nonceResponse.IsSuccessStatusCode, "server failed when getting nonce");
            var clientNonce = nonceResponse.Content;

            //HACK: need to refactor types and drop the clientnoncepackage
            var nonce = new NonceData(clientNonce!.SaltPassword64, clientNonce.SaltKek64, clientNonce.PublicPem, clientNonce.CRC)
            {
                Nonce64 = clientNonce.Nonce64
            };
            var reply = PasswordDataManager.CalculatePasswordReply(password, nonce);
            var response = await svc.Authenticate(reply);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to authenticate {identity}");
            Assert.That(response.Content, Is.Not.Null);

            var ownerAuthenticationResult = response.Content;

            var cookies = jar.GetCookies(authClient.BaseAddress);
            var tokenCookie = HttpUtility.UrlDecode(cookies[OwnerAuthConstants.CookieName]?.Value);

            Assert.IsTrue(ClientAuthenticationToken.TryParse(tokenCookie, out var result), "invalid authentication cookie returned");

            var newToken = result.Id;
            Assert.IsTrue(newToken != Guid.Empty);
            Assert.IsTrue(result.AccessTokenHalfKey.IsSet());
            return (result, ownerAuthenticationResult.SharedSecret.ToSensitiveByteArray());
        }

        private async Task<OwnerAuthTokenContext> GetOwnerAuthContext(OdinId identity)
        {
            if (_ownerLoginTokens.TryGetValue(identity, out var context))
            {
                return await Task.FromResult(context);
            }

            throw new Exception($"No token found for {identity}");
        }

        public async Task SetupOwnerAccount(OdinId identity, bool initializeIdentity)
        {
            const string password = "EnSøienØ";
            await this.ForceNewPassword(identity, password);

            var (result, sharedSecret) = await this.LoginToOwnerConsole(identity, this._password);

            var context = new OwnerAuthTokenContext()
            {
                AuthenticationResult = result,
                SharedSecret = sharedSecret
            };

            _ownerLoginTokens.Add(identity, context);

            if (initializeIdentity)
            {
                using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
                {
                    var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);
                    var setupConfig = new InitialSetupRequest();
                    await svc.InitializeIdentity(setupConfig);
                }
            }
        }

        public HttpClient CreateOwnerApiHttpClient(TestIdentity identity, out SensitiveByteArray sharedSecret, FileSystemType fileSystemType = FileSystemType.Standard)
        {
            return this.CreateOwnerApiHttpClient(identity.OdinId, out sharedSecret, fileSystemType);
        }

        public HttpClient CreateOwnerApiHttpClient(OdinId identity, out SensitiveByteArray sharedSecret, FileSystemType fileSystemType = FileSystemType.Standard)
        {
            var token = GetOwnerAuthContext(identity).ConfigureAwait(false).GetAwaiter().GetResult();
            var client = CreateOwnerApiHttpClient(identity, token.AuthenticationResult, token.SharedSecret, fileSystemType);
            sharedSecret = token.SharedSecret;
            return client;
        }

        public HttpClient CreateOwnerApiHttpClient(OdinId identity, ClientAuthenticationToken token, SensitiveByteArray sharedSecret, FileSystemType fileSystemType)
        {
            var cookieJar = new CookieContainer();
            cookieJar.Add(new Cookie(OwnerAuthConstants.CookieName, token.ToString(), null, identity));

            var sharedSecretGetRequestHandler = new SharedSecretGetRequestHandler(sharedSecret)
            {
                CookieContainer = cookieJar
            };

            HttpClient client = new(sharedSecretGetRequestHandler);
            client.DefaultRequestHeaders.Add(DotYouHeaderNames.FileSystemTypeHeader, Enum.GetName(typeof(FileSystemType), fileSystemType));
            client.Timeout = TimeSpan.FromMinutes(15);

            client.BaseAddress = new Uri($"https://{identity}");
            return client;
        }

        public async Task<RedactedAppRegistration> AddAppWithAllDrivePermissions(OdinId identity,
            Guid appId,
            TargetDrive targetDrive,
            bool createDrive = false,
            bool canReadConnections = false,
            bool driveAllowAnonymousReads = false,
            bool ownerOnlyDrive = false,
            List<Guid> authorizedCircles = null,
            PermissionSetGrantRequest circleMemberGrantRequest = null)
        {
            PermissionSet permissionSet;

            if (canReadConnections)
            {
                List<int> keys = new List<int>();

                keys.Add(PermissionKeys.ReadConnections);
                keys.Add(PermissionKeys.ReadConnectionRequests);
                permissionSet = new PermissionSet(keys.ToArray());
            }
            else
            {
                permissionSet = new PermissionSet();
            }


            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var drives = new List<DriveGrantRequest>();
                if (createDrive)
                {
                    var driveSvc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);
                    var createDriveResponse = await driveSvc.CreateDrive(
                        new CreateDriveRequest()
                        {
                            TargetDrive = targetDrive,
                            Name = $"Test Drive name with type {targetDrive.Type}",
                            Metadata = "{data:'test metadata'}",
                            AllowAnonymousReads = driveAllowAnonymousReads,
                            OwnerOnly = ownerOnlyDrive
                        });

                    Assert.IsTrue(createDriveResponse.IsSuccessStatusCode, $"Failed to create drive.  Response was {createDriveResponse.StatusCode}");

                    drives.Add(new DriveGrantRequest()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = targetDrive,
                            Permission = DrivePermission.All
                        }
                    });
                }

                var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

                var request = new AppRegistrationRequest
                {
                    Name = $"Test_{appId}",
                    AppId = appId,
                    PermissionSet = permissionSet,
                    Drives = drives,
                    AuthorizedCircles = authorizedCircles,
                    CircleMemberPermissionGrant = circleMemberGrantRequest
                };

                var response = await svc.RegisterApp(request);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                Assert.IsNotNull(appReg);

                var updatedAppResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = appId });
                Assert.That(updatedAppResponse.IsSuccessStatusCode, Is.True);
                Assert.That(updatedAppResponse.Content, Is.Not.Null);

                return updatedAppResponse.Content;
            }
        }

        public async Task<(ClientAuthenticationToken clientAuthToken, byte[] sharedSecret)> AddAppClient(OdinId identity, Guid appId)
        {
            var rsa = new RsaFullKeyData(ref RsaKeyListManagement.zeroSensitiveKey, 1); // TODO

            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

                var request = new AppClientRegistrationRequest()
                {
                    AppId = appId,
                    ClientPublicKey64 = Convert.ToBase64String(rsa.publicKey),
                    ClientFriendlyName = "Some phone"
                };

                var regResponse = await svc.RegisterAppOnClient(request);
                Assert.IsTrue(regResponse.IsSuccessStatusCode);
                Assert.IsNotNull(regResponse.Content);

                var reply = regResponse.Content;
                var decryptedData = rsa.Decrypt(ref RsaKeyListManagement.zeroSensitiveKey, reply.Data); // TODO

                //only supporting version 1 for now
                Assert.That(reply.EncryptionVersion, Is.EqualTo(1));
                Assert.That(reply.Token, Is.Not.EqualTo(Guid.Empty));
                Assert.That(decryptedData, Is.Not.Null);
                Assert.That(decryptedData.Length, Is.EqualTo(49));

                var cat = ClientAccessToken.FromPortableBytes(decryptedData);
                Assert.IsFalse(cat.Id == Guid.Empty);
                Assert.IsNotNull(cat.AccessTokenHalfKey);
                Assert.That(cat.AccessTokenHalfKey.GetKey().Length, Is.EqualTo(16));
                Assert.IsTrue(cat.AccessTokenHalfKey.IsSet());
                Assert.IsTrue(cat.IsValid());

                return (cat.ToAuthenticationToken(), cat.SharedSecret.GetKey());
            }
        }

        public async Task RevokeSampleApp(OdinId identity, Guid appId)
        {
            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

                await svc.RevokeApp(new GetAppRequest() { AppId = appId });
            }
        }

        public async Task UpdateAppAuthorizedCircles(OdinId identity, Guid appId, List<Guid> authorizedCircles, PermissionSetGrantRequest grant)
        {
            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

                await svc.UpdateAuthorizedCircles(new UpdateAuthorizedCirclesRequest()
                {
                    AppId = appId,
                    AuthorizedCircles = authorizedCircles,
                    CircleMemberPermissionGrant = grant
                });
            }
        }

        public async Task UpdateAppPermissions(OdinId identity, Guid appId, PermissionSetGrantRequest grant)
        {
            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

                await svc.UpdateAppPermissions(new UpdateAppPermissionsRequest()
                {
                    AppId = appId,
                    Drives = grant.Drives,
                    PermissionSet = grant.PermissionSet
                });
            }
        }

        /// <summary>
        /// Creates an app, device, and logs in returning an contextual information needed to run unit tests.
        /// </summary>
        /// <returns></returns>
        public async Task<TestAppContext> SetupTestSampleApp(
            Guid appId,
            TestIdentity identity,
            bool canReadConnections = false,
            TargetDrive targetDrive = null,
            bool driveAllowAnonymousReads = false,
            bool ownerOnlyDrive = false,
            List<Guid> authorizedCircles = null,
            PermissionSetGrantRequest circleMemberGrantRequest = null)
        {
            if (null == targetDrive)
            {
                targetDrive = new TargetDrive()
                {
                    Alias = Guid.NewGuid(),
                    Type = Guid.NewGuid()
                };
            }

            await this.AddAppWithAllDrivePermissions(identity.OdinId, appId, targetDrive, true, canReadConnections, driveAllowAnonymousReads, ownerOnlyDrive, authorizedCircles, circleMemberGrantRequest);

            var (authResult, sharedSecret) = await this.AddAppClient(identity.OdinId, appId);
            return new TestAppContext()
            {
                Identity = identity.OdinId,
                ContactData = identity.ContactData,
                AppId = appId,
                ClientAuthenticationToken = authResult,
                SharedSecret = sharedSecret,
                TargetDrive = targetDrive
            };
        }
        

        public async Task InitializeIdentity(TestIdentity identity, InitialSetupRequest setupConfig)
        {
            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IOwnerConfigurationClient>(client, ownerSharedSecret);
                var initIdentityResponse = await svc.InitializeIdentity(setupConfig);
                Assert.IsTrue(initIdentityResponse.IsSuccessStatusCode);

                var getIsIdentityConfiguredResponse = await svc.IsIdentityConfigured();
                Assert.IsTrue(getIsIdentityConfiguredResponse.IsSuccessStatusCode);
                Assert.IsTrue(getIsIdentityConfiguredResponse.Content);
            }
        }

        public async Task<TestAppContext> SetupTestSampleApp(TestIdentity identity, bool ownerOnlyDrive = false)
        {
            Guid appId = Guid.NewGuid();
            TargetDrive targetDrive = new TargetDrive()
            {
                Alias = Guid.NewGuid(),
                Type = Guid.NewGuid()
            };
            return await this.SetupTestSampleApp(appId, identity, false, targetDrive, ownerOnlyDrive: ownerOnlyDrive);
        }

        public void SetupTestSampleApp(TestIdentity identity, InitialSetupRequest setupConfig)
        {
        }

        public async Task DisconnectIdentities(OdinId odinId1, OdinId odinId2)
        {
            using (var client = this.CreateOwnerApiHttpClient(odinId1, out var ownerSharedSecret))
            {
                var disconnectResponse = await RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret).Disconnect(new OdinIdRequest() { OdinId = odinId2 });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Samwise.OdinId, ConnectionStatus.None);
            }

            using (var client = this.CreateOwnerApiHttpClient(odinId2, out var ownerSharedSecret))
            {
                var disconnectResponse = await RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret).Disconnect(new OdinIdRequest() { OdinId = odinId1 });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Frodo.OdinId, ConnectionStatus.None);
            }
        }

        public async Task ProcessOutbox(OdinId sender, int batchSize = 1)
        {
            using (var client = CreateOwnerApiHttpClient(sender, out var ownerSharedSecret))
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                client.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());
                var resp = await transitSvc.ProcessOutbox(batchSize);
                Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
            }
        }

        private async Task AssertConnectionStatus(HttpClient client, SensitiveByteArray ownerSharedSecret, string odinId, ConnectionStatus expected)
        {
            var svc = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
            var response = await svc.GetConnectionInfo(new OdinIdRequest() { OdinId = odinId });

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {odinId}.  Status code was {response.StatusCode}");
            Assert.IsNotNull(response.Content, $"No status for {odinId} found");
            Assert.IsTrue(response.Content.Status == expected, $"{odinId} status does not match {expected}");
        }

        public async Task CreateConnection(OdinId sender, OdinId recipient, CreateConnectionOptions createConnectionOptions = null)
        {
            if (!TestIdentities.All.TryGetValue(sender, out var senderIdentity))
            {
                throw new NotImplementedException("need to add your sender to the list of identities");
            }

            if (!TestIdentities.All.TryGetValue(recipient, out var recipientIdentity))
            {
                throw new NotImplementedException("need to add your recipient to the list of identities");
            }

            var co = createConnectionOptions ?? new CreateConnectionOptions();
            //have frodo send it
            using (var client = this.CreateOwnerApiHttpClient(sender, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var id = Guid.NewGuid();
                var requestHeader = new ConnectionRequestHeader()
                {
                    Id = id,
                    Recipient = recipient,
                    Message = "Please add me",
                    ContactData = senderIdentity.ContactData,
                    CircleIds = co.CircleIdsGrantedToRecipient
                };

                var response = await svc.SendConnectionRequest(requestHeader);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
                Assert.IsTrue(response!.Content, "Failed sending the request");
            }

            //accept the request
            using (var client = this.CreateOwnerApiHttpClient(recipient, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleNetworkRequestsOwnerClient>(client, ownerSharedSecret);

                var header = new AcceptRequestHeader()
                {
                    Sender = sender,
                    CircleIds = co.CircleIdsGrantedToSender,
                    ContactData = recipientIdentity.ContactData
                };
                var acceptResponse = await svc.AcceptConnectionRequest(header);
                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");
            }
        }

        public async Task<bool> IsConnected(OdinId sender, OdinId recipient)
        {
            using (var client = this.CreateOwnerApiHttpClient(sender, out var ownerSharedSecret))
            {
                var connectionsService = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
                var existingConnectionInfo = await connectionsService.GetConnectionInfo(new OdinIdRequest() { OdinId = recipient });
                if (existingConnectionInfo.IsSuccessStatusCode && existingConnectionInfo.Content != null && existingConnectionInfo.Content.Status == ConnectionStatus.Connected)
                {
                    return true;
                }
            }

            return false;
        }

        public async Task CreateDrive(OdinId identity, TargetDrive targetDrive, string name, string metadata, bool allowAnonymousReads, bool ownerOnly = false)
        {
            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);

                if (ownerOnly && allowAnonymousReads)
                {
                    throw new Exception("cannot have an owner only drive that allows anonymous reads");
                }

                var response = await svc.CreateDrive(new CreateDriveRequest()
                {
                    TargetDrive = targetDrive,
                    Name = name,
                    Metadata = metadata,
                    AllowAnonymousReads = allowAnonymousReads,
                    OwnerOnly = ownerOnly
                });

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                Assert.IsNotNull(response.Content);

                var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });

                Assert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
                var page = getDrivesResponse.Content;

                Assert.NotNull(page);
                Assert.NotNull(page.Results.SingleOrDefault(drive => drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type));
            }
        }

        public async Task EnsureDriveExists(OdinId identity, TargetDrive targetDrive, bool allowAnonymousReads)
        {
            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                //ensure drive
                var svc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);
                var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
                Assert.IsNotNull(getDrivesResponse.Content);
                var drives = getDrivesResponse.Content.Results;
                var exists = drives.Any(d => d.TargetDriveInfo.Alias == targetDrive.Alias && d.TargetDriveInfo.Type == targetDrive.Type);

                if (!exists)
                {
                    await this.CreateDrive(identity, targetDrive, "test drive", "", allowAnonymousReads);
                }
            }
        }

        public async Task<UploadTestUtilsContext> Upload(OdinId identity, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options = null)
        {
            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var targetDrive = new TargetDrive()
            {
                Alias = Guid.Parse("99888555-0000-0000-0000-000000004445"),
                Type = Guid.NewGuid()
            };

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    Drive = targetDrive,
                    OverwriteFileId = null
                },
                TransitOptions = null
            };

            return await TransferFile(identity, instructionSet, fileMetadata, options ?? TransitTestUtilsOptions.Default);
        }

        public async Task<UploadTestUtilsContext> UploadFile(OdinId identity, UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata, string payloadData,
            bool encryptPayload = true, ImageDataContent thumbnail = null, KeyHeader keyHeader = null, FileSystemType fileSystemType = FileSystemType.Standard)
        {
            Assert.IsNull(instructionSet.TransitOptions?.Recipients, "This method will not send transfers; please ensure recipients are null");

            await this.EnsureDriveExists(identity, instructionSet.StorageOptions.Drive, false);

            using (var client = this.CreateOwnerApiHttpClient(identity, out var sharedSecret, fileSystemType))
            {
                keyHeader = keyHeader ?? KeyHeader.NewRandom16();
                var instructionStream = new MemoryStream(DotYouSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

                fileMetadata.PayloadIsEncrypted = encryptPayload;
                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                    FileMetadata = fileMetadata
                };

                var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);
                var payloadCipherBytes = keyHeader.EncryptDataAes(payloadData.ToUtf8ByteArray());
                var payloadCipher = encryptPayload ? new MemoryStream(payloadCipherBytes) : new MemoryStream(payloadData.ToUtf8ByteArray());
                var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);

                ApiResponse<UploadResult> response;
                if (thumbnail == null)
                {
                    response = await transitSvc.Upload(
                        new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                        new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                        new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));
                }
                else
                {
                    var thumbnailCipherBytes = encryptPayload ? keyHeader.EncryptDataAesAsStream(thumbnail.Content) : new MemoryStream(thumbnail.Content);
                    response = await transitSvc.Upload(
                        new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                        new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                        new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
                        new StreamPart(thumbnailCipherBytes, thumbnail.GetFilename(), thumbnail.ContentType, Enum.GetName(MultipartUploadParts.Thumbnail)));
                }

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(transferResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

                //keyHeader.AesKey.Wipe();

                return new UploadTestUtilsContext()
                {
                    InstructionSet = instructionSet,
                    UploadFileMetadata = fileMetadata,
                    PayloadData = payloadData,
                    UploadedFile = transferResult.File,
                    PayloadCipher = payloadCipherBytes,
                    FileSystemType = fileSystemType
                };
            }
        }

        private async Task<UploadTestUtilsContext> TransferFile(OdinId sender, UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options)
        {
            var recipients = instructionSet.TransitOptions?.Recipients ?? new List<string>();

            if (options.ProcessTransitBox & (recipients.Count == 0 || options.ProcessOutbox == false))
            {
                throw new Exception("Options not valid.  There must be at least one recipient and ProcessOutbox must be true when ProcessTransitBox is set to true");
            }

            var targetDrive = instructionSet.StorageOptions.Drive;

            //Feature added much later in schedule but it means we don't have to thread sleep in our unit tests
            if (options.ProcessOutbox && instructionSet.TransitOptions != null)
            {
                instructionSet.TransitOptions.Schedule = ScheduleOptions.SendNowAwaitResponse;
            }

            await this.EnsureDriveExists(sender, targetDrive, options.DriveAllowAnonymousReads);

            //Setup the drives on all recipient DIs
            foreach (var r in recipients)
            {
                var recipient = (OdinId)r;
                await this.EnsureDriveExists(recipient, targetDrive, options.DriveAllowAnonymousReads);
                //Note: this connection needs access to write to the targetDrive on the recipient server
                await this.CreateConnection(sender, recipient);
            }

            var payloadData = options?.PayloadData ?? "{payload:true, image:'b64 data'}";

            using (var client = this.CreateOwnerApiHttpClient(sender, out var sharedSecret))
            {
                var keyHeader = KeyHeader.NewRandom16();
                var transferIv = instructionSet.TransferIv;

                var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                fileMetadata.PayloadIsEncrypted = options.EncryptPayload;
                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
                    FileMetadata = fileMetadata
                };

                var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);


                payloadData = options?.PayloadData ?? payloadData;
                Stream payloadCipher = options.EncryptPayload ? keyHeader.EncryptDataAesAsStream(payloadData) : new MemoryStream(payloadData.ToUtf8ByteArray());

                var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(transferResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

                int outboxBatchSize = 1;
                if (instructionSet.TransitOptions?.Recipients?.Any() ?? false)
                {
                    Assert.IsTrue(transferResult.RecipientStatus.Count == instructionSet.TransitOptions?.Recipients.Count, "expected recipient count does not match");

                    foreach (var recipient in instructionSet.TransitOptions?.Recipients)
                    {
                        Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");
                        Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.TransferKeyCreated, $"transfer key not created for {recipient}");
                    }

                    outboxBatchSize = transferResult.RecipientStatus.Count;
                }

                // if (options is { ProcessOutbox: true })
                // {
                //     var resp = await transitSvc.ProcessOutbox(outboxBatchSize);
                //     Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
                // }

                if (options is { ProcessTransitBox: true })
                {
                    //wait for process outbox to run
                    // Task.Delay(2000).Wait();

                    foreach (var recipient in recipients)
                    {
                        //TODO: this should be a create app http client but it works because the path on ITransitTestAppHttpClient is /apps
                        using (var rClient = CreateOwnerApiHttpClient((OdinId)recipient, out var _))
                        {
                            var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
                            rClient.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());

                            var resp = await transitAppSvc.ProcessIncomingInstructions(new ProcessTransitInstructionRequest() { TargetDrive = targetDrive });
                            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
                        }
                    }
                }

                keyHeader.AesKey.Wipe();

                return new UploadTestUtilsContext()
                {
                    InstructionSet = instructionSet,
                    UploadFileMetadata = fileMetadata,
                    PayloadData = payloadData,
                    UploadedFile = transferResult.File
                };
            }
        }

        public async Task<CircleDefinition> CreateCircleWithDrive(OdinId identity, string circleName, IEnumerable<int> permissionKeys, PermissionedDrive drive)
        {
            return await this.CreateCircleWithDrive(identity, circleName, permissionKeys, new List<PermissionedDrive>() { drive });
        }

        public async Task<CircleDefinition> CreateCircleWithDrive(OdinId identity, string circleName, IEnumerable<int> permissionKeys, List<PermissionedDrive> drives)
        {
            using (var client = CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<ICircleDefinitionOwnerClient>(client, ownerSharedSecret);

                var dgrList = drives.Select(d => new DriveGrantRequest()
                {
                    PermissionedDrive = d
                }).ToList();

                var request = new CreateCircleRequest()
                {
                    Id = Guid.NewGuid(),
                    Name = circleName,
                    Description = $"Description for {circleName}",
                    DriveGrants = dgrList,
                    Permissions = permissionKeys?.Any() ?? false ? new PermissionSet(permissionKeys?.ToArray()) : new PermissionSet()
                };

                var createCircleResponse = await svc.CreateCircleDefinition(request);
                Assert.IsTrue(createCircleResponse.IsSuccessStatusCode, $"Failed.  Actual response {createCircleResponse.StatusCode}");

                var getCircleDefinitionsResponse = await svc.GetCircleDefinitions();
                Assert.IsTrue(getCircleDefinitionsResponse.IsSuccessStatusCode, $"Failed.  Actual response {getCircleDefinitionsResponse.StatusCode}");

                var definitionList = getCircleDefinitionsResponse.Content;
                Assert.IsNotNull(definitionList);

                var circle = definitionList.Single(c => c.Id == request.Id);

                foreach (var dgr in dgrList)
                {
                    Assert.IsNotNull(circle.DriveGrants.SingleOrDefault(d => d == dgr));
                }

                foreach (var k in permissionKeys)
                {
                    Assert.IsTrue(circle.Permissions.HasKey(k));
                }

                Assert.AreEqual(request.Name, circle.Name);
                Assert.AreEqual(request.Description, circle.Description);
                Assert.IsTrue(request.Permissions == circle.Permissions);

                return circle;
            }
        }
    }

    public class CreateConnectionOptions
    {
        public List<GuidId> CircleIdsGrantedToRecipient { get; set; } = new();

        public List<GuidId> CircleIdsGrantedToSender { get; set; } = new();
    }
}