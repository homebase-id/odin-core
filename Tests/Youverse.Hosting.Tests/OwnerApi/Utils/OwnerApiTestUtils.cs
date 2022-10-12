﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
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
using Youverse.Core.Services.Configuration;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Controllers;
using Youverse.Hosting.Controllers.ClientToken.Transit;
using Youverse.Hosting.Controllers.OwnerToken.AppManagement;
using Youverse.Hosting.Controllers.OwnerToken.Drive;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.OwnerApi.Apps;
using Youverse.Hosting.Tests.OwnerApi.Authentication;
using Youverse.Hosting.Tests.OwnerApi.Circle;
using Youverse.Hosting.Tests.OwnerApi.Configuration;
using Youverse.Hosting.Tests.OwnerApi.Drive;

namespace Youverse.Hosting.Tests.OwnerApi.Utils
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

        public async Task SetupOwnerAccount(DotYouIdentity identity, bool initializeIdentity)
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

        public HttpClient CreateOwnerApiHttpClient(DotYouIdentity identity)
        {
            var token = GetOwnerAuthContext(identity).ConfigureAwait(false).GetAwaiter().GetResult();
            var client = CreateOwnerApiHttpClient(identity, token.AuthenticationResult, token.SharedSecret);
            return client;
        }

        public HttpClient CreateOwnerApiHttpClient(TestIdentity identity, out SensitiveByteArray sharedSecret)
        {
            return this.CreateOwnerApiHttpClient(identity.DotYouId, out sharedSecret);
        }

        public HttpClient CreateOwnerApiHttpClient(DotYouIdentity identity, out SensitiveByteArray sharedSecret)
        {
            var token = GetOwnerAuthContext(identity).ConfigureAwait(false).GetAwaiter().GetResult();
            var client = CreateOwnerApiHttpClient(identity, token.AuthenticationResult, token.SharedSecret);
            sharedSecret = token.SharedSecret;
            return client;
        }

        public HttpClient CreateOwnerApiHttpClient(DotYouIdentity identity, ClientAuthenticationToken token, SensitiveByteArray sharedSecret)
        {
            var cookieJar = new CookieContainer();
            cookieJar.Add(new Cookie(OwnerAuthConstants.CookieName, token.ToString(), null, identity));

            HttpMessageHandler cookieHandler = new HttpClientHandler()
            {
                CookieContainer = cookieJar
            };

            // var handler = new SharedSecretHandler(sharedSecret, cookieHandler);

            HttpClient client = new(cookieHandler);
            client.Timeout = TimeSpan.FromMinutes(15);

            client.BaseAddress = new Uri($"https://{identity}");
            return client;
        }

        public async Task<RedactedAppRegistration> AddApp(DotYouIdentity identity, Guid appId, TargetDrive targetDrive, bool createDrive = false, bool canReadConnections = false,
            bool driveAllowAnonymousReads = false, bool ownerOnlyDrive = false)
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
                            Permission = DrivePermission.Read
                        }
                    });
                }

                var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

                var request = new AppRegistrationRequest
                {
                    Name = $"Test_{appId}",
                    AppId = appId,
                    PermissionSet = permissionSet,
                    Drives = drives
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

        public async Task<(ClientAuthenticationToken clientAuthToken, byte[] sharedSecret)> AddAppClient(DotYouIdentity identity, Guid appId)
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

                var (tokenPortableBytes, sharedSecret) = ByteArrayUtil.Split(decryptedData, 33, 16);

                ClientAuthenticationToken authenticationResult = ClientAuthenticationToken.FromPortableBytes(tokenPortableBytes);

                Assert.False(authenticationResult.Id == Guid.Empty);
                Assert.IsNotNull(authenticationResult.AccessTokenHalfKey);
                Assert.That(authenticationResult.AccessTokenHalfKey.GetKey().Length, Is.EqualTo(16));
                Assert.IsTrue(authenticationResult.AccessTokenHalfKey.IsSet());

                Assert.IsNotNull(sharedSecret);
                Assert.That(sharedSecret.Length, Is.EqualTo(16));

                return (authenticationResult, sharedSecret);
            }
        }

        public async Task RevokeSampleApp(DotYouIdentity identity, Guid appId)
        {
            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var svc = RefitCreator.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

                await svc.RevokeApp(new GetAppRequest() { AppId = appId });
            }
        }


        /// <summary>
        /// Creates an app, device, and logs in returning an contextual information needed to run unit tests.
        /// </summary>
        /// <returns></returns>
        public async Task<TestSampleAppContext> SetupTestSampleApp(TestIdentity identity, bool ownerOnlyDrive = false)
        {
            Guid appId = Guid.NewGuid();
            TargetDrive targetDrive = new TargetDrive()
            {
                Alias = Guid.NewGuid(),
                Type = Guid.NewGuid()
            };
            return await this.SetupTestSampleApp(appId, identity, false, targetDrive, ownerOnlyDrive: ownerOnlyDrive);
        }

        public async Task<TestSampleAppContext> SetupTestSampleApp(Guid appId, TestIdentity identity, bool canReadConnections = false, TargetDrive targetDrive = null,
            bool driveAllowAnonymousReads = false, bool ownerOnlyDrive = false)
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

            this.AddApp(identity.DotYouId, appId, targetDrive, true, canReadConnections, driveAllowAnonymousReads, ownerOnlyDrive).GetAwaiter().GetResult();

            var (authResult, sharedSecret) = this.AddAppClient(identity.DotYouId, appId).GetAwaiter().GetResult();
            return new TestSampleAppContext()
            {
                Identity = identity.DotYouId,
                ContactData = identity.ContactData,
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
            //         JsonContent = options?.AppDataJsonContent ?? DotYouSystemSerializer.Serialize(new { message = "We're going to the beach; this is encrypted by the app" })
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

        public async Task DisconnectIdentities(DotYouIdentity dotYouId1, DotYouIdentity dotYouId2)
        {
            using (var client = this.CreateOwnerApiHttpClient(dotYouId1, out var ownerSharedSecret))
            {
                var disconnectResponse = await RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret).Disconnect(new DotYouIdRequest() { DotYouId = dotYouId2 });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Samwise.DotYouId, ConnectionStatus.None);
            }

            using (var client = this.CreateOwnerApiHttpClient(dotYouId2, out var ownerSharedSecret))
            {
                var disconnectResponse = await RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret).Disconnect(new DotYouIdRequest() { DotYouId = dotYouId1 });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, TestIdentities.Frodo.DotYouId, ConnectionStatus.None);
            }
        }

        public async Task ProcessOutbox(DotYouIdentity sender, int batchSize = 1)
        {
            using (var client = CreateOwnerApiHttpClient(sender, out var ownerSharedSecret))
            {
                var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);
                client.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());
                var resp = await transitSvc.ProcessOutbox(batchSize);
                Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
            }
        }

        private async Task AssertConnectionStatus(HttpClient client, SensitiveByteArray ownerSharedSecret, string dotYouId, ConnectionStatus expected)
        {
            var svc = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
            var response = await svc.GetConnectionInfo(new DotYouIdRequest() { DotYouId = dotYouId });

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get status for {dotYouId}.  Status code was {response.StatusCode}");
            Assert.IsNotNull(response.Content, $"No status for {dotYouId} found");
            Assert.IsTrue(response.Content.Status == expected, $"{dotYouId} status does not match {expected}");
        }

        public async Task CreateConnection(DotYouIdentity sender, DotYouIdentity recipient)
        {
            if (!TestIdentities.All.TryGetValue(sender, out var senderIdentity))
            {
                throw new NotImplementedException("need to add your sender to the list of identities");
            }

            if (!TestIdentities.All.TryGetValue(recipient, out var recipientIdentity))
            {
                throw new NotImplementedException("need to add your recipient to the list of identities");
            }

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
                    ContactData = senderIdentity.ContactData
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
                    CircleIds = new List<GuidId>(),
                    ContactData = recipientIdentity.ContactData
                };
                var acceptResponse = await svc.AcceptConnectionRequest(header);
                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");
            }
        }

        public async Task<bool> IsConnected(DotYouIdentity sender, DotYouIdentity recipient)
        {
            using (var client = this.CreateOwnerApiHttpClient(sender, out var ownerSharedSecret))
            {
                var connectionsService = RefitCreator.RestServiceFor<ICircleNetworkConnectionsOwnerClient>(client, ownerSharedSecret);
                var existingConnectionInfo = await connectionsService.GetConnectionInfo(new DotYouIdRequest() { DotYouId = recipient });
                if (existingConnectionInfo.IsSuccessStatusCode && existingConnectionInfo.Content != null && existingConnectionInfo.Content.Status == ConnectionStatus.Connected)
                {
                    return true;
                }
            }

            return false;
        }

        public async Task CreateDrive(DotYouIdentity identity, TargetDrive targetDrive, string name, string metadata, bool allowAnonymousReads, bool ownerOnly = false)
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

        public async Task EnsureDriveExists(DotYouIdentity identity, TargetDrive targetDrive, bool allowAnonymousReads)
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
                    OverwriteFileId = null
                },
                TransitOptions = null
            };

            return await TransferFile(identity, instructionSet, fileMetadata, options ?? TransitTestUtilsOptions.Default);
        }

        public async Task<UploadTestUtilsContext> UploadFile(DotYouIdentity identity, UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata, string payloadData,
            bool encryptPayload = true)
        {
            Assert.IsNull(instructionSet.TransitOptions?.Recipients, "This method will not send transfers; please ensure recipients are null");

            await this.EnsureDriveExists(identity, instructionSet.StorageOptions.Drive, false);

            using (var client = this.CreateOwnerApiHttpClient(identity, out var sharedSecret))
            {
                var keyHeader = KeyHeader.NewRandom16();
                var instructionStream = new MemoryStream(DotYouSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

                fileMetadata.PayloadIsEncrypted = encryptPayload;
                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                    FileMetadata = fileMetadata
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

                var payloadCipher = encryptPayload ? keyHeader.EncryptDataAesAsStream(payloadData) : new MemoryStream(payloadData.ToUtf8ByteArray());

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
                    UploadFileMetadata = fileMetadata,
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

                var bytes = System.Text.Encoding.UTF8.GetBytes(DotYouSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                fileMetadata.PayloadIsEncrypted = options.EncryptPayload;
                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref sharedSecret),
                    FileMetadata = fileMetadata
                };

                var fileDescriptorCipher = Utilsx.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);


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
                    var resp = await transitSvc.ProcessOutbox(options.OutboxProcessingBatchSize);
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
                            rClient.DefaultRequestHeaders.Add("SY4829", Guid.Parse("a1224889-c0b1-4298-9415-76332a9af80e").ToString());

                            var resp = await transitAppSvc.ProcessIncomingTransfers(new ProcessTransfersRequest() { TargetDrive = targetDrive });
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
    }
}