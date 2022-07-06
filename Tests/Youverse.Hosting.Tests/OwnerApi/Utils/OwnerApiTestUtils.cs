using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Controllers.OwnerToken.AppManagement;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Circle;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.OwnerApi.Apps;
using Youverse.Hosting.Tests.OwnerApi.Authentication;
using Youverse.Hosting.Tests.OwnerApi.Circle;
using Youverse.Hosting.Tests.OwnerApi.Drive;
using Youverse.Hosting.Tests.OwnerApi.Provisioning;

namespace Youverse.Hosting.Tests.OwnerApi.Scaffold
{
    public class OwnerApiTestUtils
    {
        private readonly string _password = "EnSøienØ";
        private readonly Dictionary<string, OwnerAuthTokenContext> _ownerLoginTokens = new(StringComparer.InvariantCultureIgnoreCase);

        public async Task ForceNewPassword(string identity, string password)
        {
            var handler = new HttpClientHandler();
            var jar = new CookieContainer();
            handler.CookieContainer = jar;
            handler.UseCookies = true;

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

        private async Task<OwnerAuthTokenContext> GetOwnerAuthContext(DotYouIdentity identity)
        {
            if (_ownerLoginTokens.TryGetValue(identity, out var context))
            {
                return context;
            }

            throw new Exception($"No token found for {identity}");
        }

        public async Task SetupOwnerAccount(DotYouIdentity identity)
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

            using (var client = this.CreateOwnerApiHttpClient(identity, out var _))
            {
                var svc = RestService.For<IProvisioningClient>(client);
                await svc.EnsureSystemApps();
            }
        }

        public HttpClient CreateOwnerApiHttpClient(DotYouIdentity identity)
        {
            var token = GetOwnerAuthContext(identity).ConfigureAwait(false).GetAwaiter().GetResult();
            var client = CreateOwnerApiHttpClient(identity, token.AuthenticationResult, null);
            return client;
        }

        public HttpClient CreateOwnerApiHttpClient(DotYouIdentity identity, out SensitiveByteArray sharedSecret)
        {
            var token = GetOwnerAuthContext(identity).ConfigureAwait(false).GetAwaiter().GetResult();
            var client = CreateOwnerApiHttpClient(identity, token.AuthenticationResult, null);
            sharedSecret = token.SharedSecret;
            return client;
        }

        public HttpClient CreateOwnerApiHttpClient(DotYouIdentity identity, ClientAuthenticationToken token, Guid? appId = null)
        {
            var cookieJar = new CookieContainer();
            cookieJar.Add(new Cookie(OwnerAuthConstants.CookieName, token.ToString(), null, identity));
            HttpMessageHandler handler = new HttpClientHandler()
            {
                CookieContainer = cookieJar
            };

            HttpClient client = new(handler);
            client.Timeout = TimeSpan.FromMinutes(15);

            if (appId != null)
            {
                client.DefaultRequestHeaders.Add(DotYouHeaderNames.AppId, appId.ToString());
            }

            client.BaseAddress = new Uri($"https://{identity}");
            return client;
        }

        public async Task<AppRegistrationResponse> AddApp(DotYouIdentity identity, Guid appId, TargetDrive targetDrive, bool createDrive = false, bool canManageConnections = false,
            bool driveAllowAnonymousReads = false)
        {
            var permissionSet = new PermissionSet();
            if (canManageConnections)
            {
                permissionSet.Permissions.Add(SystemApi.CircleNetwork, (int)CircleNetworkPermissions.Manage);
                permissionSet.Permissions.Add(SystemApi.CircleNetworkRequests, (int)CircleNetworkRequestPermissions.Manage);
            }

            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var drives = new List<TargetDrive>();
                if (createDrive)
                {
                    var driveSvc = RestService.For<IDriveManagementHttpClient>(client);
                    var createDriveResponse = await driveSvc.CreateDrive(targetDrive, $"Test Drive name with type {targetDrive.Type}", "{data:'test metadata'}", driveAllowAnonymousReads);
                    Assert.IsTrue(createDriveResponse.IsSuccessStatusCode, $"Failed to create drive.  Response was {createDriveResponse.StatusCode}");
                    
                    drives.Add(targetDrive);

                }

                var svc = RestService.For<IAppRegistrationClient>(client);
                var request = new AppRegistrationRequest
                {
                    Name = $"Test_{appId}",
                    ApplicationId = appId,
                    PermissionSet = permissionSet,
                    Drives = drives
                };

                var response = await svc.RegisterApp(request);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                Assert.IsNotNull(appReg);

                var updatedAppResponse = await svc.GetRegisteredApp(appId);
                Assert.That(updatedAppResponse.IsSuccessStatusCode, Is.True);
                Assert.That(updatedAppResponse.Content, Is.Not.Null);

                return updatedAppResponse.Content;
            }
        }

        public async Task<(ClientAuthenticationToken clientAuthToken, byte[] sharedSecret)> AddAppClient(DotYouIdentity identity, Guid appId)
        {
            var rsa = new RsaFullKeyData(ref RsaKeyListManagement.zeroSensitiveKey, 1); // TODO

            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RestService.For<IAppRegistrationClient>(client);

                var request = new AppClientRegistrationRequest()
                {
                    ApplicationId = appId,
                    ClientPublicKey64 = Convert.ToBase64String(rsa.publicKey)
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
                Assert.That(decryptedData.Length, Is.EqualTo(48));

                var (idBytes, clientAccessHalfKey, sharedSecret) = ByteArrayUtil.Split(decryptedData, 16, 16, 16);

                Assert.False(new Guid(idBytes) == Guid.Empty);
                Assert.IsNotNull(clientAccessHalfKey);
                Assert.IsNotNull(sharedSecret);
                Assert.That(clientAccessHalfKey.Length, Is.EqualTo(16));
                Assert.That(sharedSecret.Length, Is.EqualTo(16));

                ClientAuthenticationToken authenticationResult = new ClientAuthenticationToken()
                {
                    Id = reply.Token,
                    AccessTokenHalfKey = clientAccessHalfKey.ToSensitiveByteArray()
                };

                return (authenticationResult, sharedSecret);
            }
        }

        public async Task RevokeSampleApp(DotYouIdentity identity, Guid appId)
        {
            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RestService.For<IAppRegistrationClient>(client);
                await svc.RevokeApp(appId);
            }
        }


        /// <summary>
        /// Creates an app, device, and logs in returning an contextual information needed to run unit tests.
        /// </summary>
        /// <returns></returns>
        public async Task<TestSampleAppContext> SetupTestSampleApp(DotYouIdentity identity)
        {
            Guid appId = Guid.NewGuid();
            TargetDrive targetDrive = new TargetDrive()
            {
                Alias = Guid.NewGuid(),
                Type = Guid.NewGuid()
            };
            return await this.SetupTestSampleApp(appId, identity, false, targetDrive);
        }

        public async Task<TestSampleAppContext> SetupTestSampleApp(Guid appId, DotYouIdentity identity, bool canManageConnections = false, TargetDrive targetDrive = null,
            bool driveAllowAnonymousReads = false)
        {
            //TODO: we might need to let the callers pass this in at some point for testing

            if (null == targetDrive)
            {
                targetDrive = new TargetDrive()
                {
                    Alias = Guid.NewGuid(),
                    Type = Guid.NewGuid()
                };
            }

            //note; this is intentionally not global

            this.AddApp(identity, appId, targetDrive, true, canManageConnections, driveAllowAnonymousReads).GetAwaiter().GetResult();

            var (authResult, sharedSecret) = this.AddAppClient(identity, appId).GetAwaiter().GetResult();
            return new TestSampleAppContext()
            {
                Identity = identity,
                AppId = appId,
                ClientAuthenticationToken = authResult,
                SharedSecret = sharedSecret,
                TargetDrive = targetDrive
            };
        }


        /// <summary>
        /// Transfers a file using default file metadata
        /// </summary>
        /// <returns></returns>
        public async Task<TransitTestUtilsContext> TransferFile(DotYouIdentity sender, List<string> recipients, TransitTestUtilsOptions options = null)
        {
            // var transferIv = ByteArrayUtil.GetRndByteArray(16);
            //
            // var instructionSet = new UploadInstructionSet()
            // {
            //     TransferIv = transferIv,
            //     StorageOptions = new StorageOptions()
            //     {
            //         Drive = TargetDrive.NewTargetDrive(),
            //         OverwriteFileId = null,
            //         ExpiresTimestamp = null,
            //     },
            //
            //     TransitOptions = new TransitOptions()
            //     {
            //         Recipients = recipients
            //     }
            // };
            //
            // List<Guid> tags = null;
            // if (options?.AppDataCategoryId != null)
            // {
            //     tags = new List<Guid>() { options.AppDataCategoryId };
            // }
            //
            // var fileMetadata = new UploadFileMetadata()
            // {
            //     ContentType = "application/json",
            //     PayloadIsEncrypted = true,
            //     AppData = new()
            //     {
            //         Tags = tags,
            //         ContentIsComplete = true,
            //         JsonContent = options?.AppDataJsonContent ?? JsonConvert.SerializeObject(new { message = "We're going to the beach; this is encrypted by the app" })
            //     }
            // };
            //
            // var o = options ?? TransitTestUtilsOptions.Default;
            //
            // var result = await TransferFile(sender, instructionSet, fileMetadata, o);
            //
            // if (o.DisconnectIdentitiesAfterTransfer)
            // {
            //     foreach (var recipient in recipients)
            //     {
            //         await this.DisconnectIdentities(sender, (DotYouIdentity)recipient);
            //     }
            // }
            //
            // return result;
            return null;
        }

        public async Task DisconnectIdentities(DotYouIdentity dotYouId1, DotYouIdentity dotYouId2)
        {
            using (var client = this.CreateOwnerApiHttpClient(dotYouId1))
            {
                var disconnectResponse = await RestService.For<ICircleNetworkConnectionsOwnerClient>(client).Disconnect(dotYouId2);
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, TestIdentities.Samwise, ConnectionStatus.None);
            }

            using (var client = this.CreateOwnerApiHttpClient(dotYouId2))
            {
                var disconnectResponse = await RestService.For<ICircleNetworkConnectionsOwnerClient>(client).Disconnect(dotYouId1);
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, TestIdentities.Frodo, ConnectionStatus.None);
            }
        }

        public async Task ProcessOutbox(DotYouIdentity sender)
        {
            using (var client = CreateOwnerApiHttpClient(sender))
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                var resp = await transitSvc.ProcessOutbox();
                Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
            }
        }

        private async Task AssertConnectionStatus(HttpClient client, string dotYouId, ConnectionStatus expected)
        {
            var svc = RestService.For<ICircleNetworkConnectionsOwnerClient>(client);
            var response = await svc.GetStatus(dotYouId);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {dotYouId}.  Status code was {response.StatusCode}");
            Assert.IsNotNull(response.Content, $"No status for {dotYouId} found");
            Assert.IsTrue(response.Content.Status == expected, $"{dotYouId} status does not match {expected}");
        }

        public async Task CreateConnection(DotYouIdentity sender, DotYouIdentity recipient)
        {
            //have frodo send it
            using (var client = this.CreateOwnerApiHttpClient(sender))
            {
                var svc = RestService.For<ICircleNetworkRequestsOwnerClient>(client);

                var id = Guid.NewGuid();
                var requestHeader = new ConnectionRequestHeader()
                {
                    Id = id,
                    Recipient = recipient,
                    Message = "Please add me"
                };

                var response = await svc.SendConnectionRequest(requestHeader);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed sending the request.  Response code was [{response.StatusCode}]");
                Assert.IsTrue(response!.Content!.Success, "Failed sending the request");
            }

            //accept the request
            using (var client = this.CreateOwnerApiHttpClient(recipient))
            {
                var svc = RestService.For<ICircleNetworkRequestsOwnerClient>(client);

                var header = new AcceptRequestHeader()
                {
                    Sender = sender,
                    Drives = new List<TargetDrive>(),
                    Permissions = new PermissionSet()
                };
                var acceptResponse = await svc.AcceptConnectionRequest(header);
                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");
            }
        }

        public async Task CreateDrive(DotYouIdentity identity, TargetDrive targetDrive, string name, string metadata, bool allowAnonymousReads)
        {
            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RestService.For<IDriveManagementHttpClient>(client);

                var response = await svc.CreateDrive(targetDrive, name, metadata, allowAnonymousReads);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                Assert.IsNotNull(response.Content);

                var getDrivesResponse = await svc.GetDrives(1, 100);
                Assert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
                var page = getDrivesResponse.Content;

                Assert.NotNull(page);
                Assert.NotNull(page.Results.SingleOrDefault(drive => drive.Alias == targetDrive.Alias && drive.Type == targetDrive.Type));
            }
        }

        public async Task EnsureDriveExists(DotYouIdentity identity, TargetDrive targetDrive, bool allowAnonymousReads)
        {
            using (var client = this.CreateOwnerApiHttpClient(identity, out var sharedSecret))
            {
                //ensure drive
                var svc = RestService.For<IDriveManagementHttpClient>(client);
                var getDrivesResponse = await svc.GetDrives(1, 100);
                Assert.IsNotNull(getDrivesResponse.Content);
                var drives = getDrivesResponse.Content.Results;
                var exists = drives.Any(d => d.Alias == targetDrive.Alias && d.Type == targetDrive.Type);

                if (!exists)
                {
                    await this.CreateDrive(identity, targetDrive, "test drive", "", allowAnonymousReads);
                }
            }
        }

        public async Task<UploadTestUtilsContext> Upload(DotYouIdentity identity, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options = null)
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
                    OverwriteFileId = null,
                    ExpiresTimestamp = null
                },
                TransitOptions = null
            };

            return await TransferFile(identity, instructionSet, fileMetadata, options ?? TransitTestUtilsOptions.Default);
        }

        public async Task<UploadTestUtilsContext> UploadFile(DotYouIdentity identity, UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata, string payloadData)
        {
            Assert.IsNull(instructionSet.TransitOptions?.Recipients, "This method will not send transfers; please ensure recipients are null");

            await this.EnsureDriveExists(identity, instructionSet.StorageOptions.Drive, false);

            using (var client = this.CreateOwnerApiHttpClient(identity, out var sharedSecret))
            {
                var keyHeader = KeyHeader.NewRandom16();
                var instructionStream = new MemoryStream(JsonConvert.SerializeObject(instructionSet).ToUtf8ByteArray());

                fileMetadata.PayloadIsEncrypted = true;
                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                    FileMetadata = fileMetadata
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

                var payloadCipher = keyHeader.GetEncryptedStreamAes(payloadData);

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

                keyHeader.AesKey.Wipe();

                return new UploadTestUtilsContext()
                {
                    InstructionSet = instructionSet,
                    FileMetadata = fileMetadata,
                    PayloadData = payloadData,
                    UploadedFile = transferResult.File
                };
            }
        }

        private async Task<UploadTestUtilsContext> TransferFile(DotYouIdentity sender, UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options)
        {
            var recipients = instructionSet.TransitOptions?.Recipients ?? new List<string>();

            if (options.ProcessTransitBox & (recipients.Count == 0 || options.ProcessOutbox == false))
            {
                throw new Exception("Options not valid.  There must be at least one recipient and ProcessOutbox must be true when ProcessTransitBox is set to true");
            }

            var targetDrive = instructionSet.StorageOptions.Drive;
            await this.EnsureDriveExists(sender, targetDrive, options.DriveAllowAnonymousReads);

            //Setup the drives on all recipient DIs
            foreach (var r in recipients)
            {
                var recipient = (DotYouIdentity)r;
                await this.EnsureDriveExists(recipient, targetDrive, options.DriveAllowAnonymousReads);
                //Note: this connection needs access to write to the targetDrive on the recipient server
                await this.CreateConnection(sender, recipient);
            }

            var payloadData = options?.PayloadData ?? "{payload:true, image:'b64 data'}";

            using (var client = this.CreateOwnerApiHttpClient(sender, out var sharedSecret))
            {
                var keyHeader = KeyHeader.NewRandom16();
                var transferIv = instructionSet.TransferIv;

                var bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                fileMetadata.PayloadIsEncrypted = true;
                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
                    FileMetadata = fileMetadata
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);

                payloadData = options?.PayloadData ?? payloadData;
                var payloadCipher = keyHeader.GetEncryptedStreamAes(payloadData);

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

                if (instructionSet.TransitOptions?.Recipients != null)
                {
                    Assert.IsTrue(transferResult.RecipientStatus.Count == instructionSet.TransitOptions?.Recipients.Count, "expected recipient count does not match");

                    foreach (var recipient in instructionSet.TransitOptions?.Recipients)
                    {
                        Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");
                        Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.TransferKeyCreated, $"transfer key not created for {recipient}");
                    }
                }

                if (options is { ProcessOutbox: true })
                {
                    var resp = await transitSvc.ProcessOutbox();
                    Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
                }

                if (options is { ProcessTransitBox: true })
                {
                    //wait for process outbox to run
                    Task.Delay(2000).Wait();

                    foreach (var recipient in recipients)
                    {
                        //TODO: this should be a create app http client but it works because the path on ITransitTestAppHttpClient is /apps
                        using (var rClient = CreateOwnerApiHttpClient((DotYouIdentity)recipient, out var _))
                        {
                            var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
                            var resp = await transitAppSvc.ProcessTransfers();
                            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
                        }
                    }
                }

                keyHeader.AesKey.Wipe();

                return new UploadTestUtilsContext()
                {
                    InstructionSet = instructionSet,
                    FileMetadata = fileMetadata,
                    PayloadData = payloadData,
                    UploadedFile = transferResult.File
                };
            }
        }
    }
}