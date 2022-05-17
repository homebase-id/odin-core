﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using NUnit.Framework;
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle.Membership;
using Youverse.Core.Services.Contacts.Circle.Requests;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Core.Util;
using Youverse.Hosting.Authentication.App;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Controllers.Owner.AppManagement;
using Youverse.Hosting.Tests.AppAPI;
using Youverse.Hosting.Tests.AppAPI.Circle;
using Youverse.Hosting.Tests.AppAPI.Transit;
using Youverse.Hosting.Tests.OwnerApi.Apps;
using Youverse.Hosting.Tests.OwnerApi.Authentication;
using Youverse.Hosting.Tests.OwnerApi.Provisioning;
using Youverse.Hosting.Tests.OwnerApi.Transit;

namespace Youverse.Hosting.Tests
{
    //Note: this class is wayyy to big, need to decompose :)
    public class TestScaffold
    {
        private readonly string _folder;
        private readonly string _password = "EnSøienØ";
        private IHost _webserver;
        private readonly Dictionary<string, OwnerAuthTokenContext> _ownerLoginTokens = new(StringComparer.InvariantCultureIgnoreCase);
        DevelopmentIdentityContextRegistry _registry;

        public TestScaffold(string folder)
        {
            this._folder = folder;
        }

        public string TestDataPath
        {
            get
            {
                var p = PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "testsdata", "dotyoudata", _folder);
                string x = isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
                return x;
            }
        }

        private bool isDev => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        private string home => Environment.GetEnvironmentVariable("HOME") ?? "";

        public string TempDataPath
        {
            get
            {
                var p = PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "tempdata", "dotyoudata", _folder);
                return isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
            }
        }

        public string LogFilePath
        {
            get
            {
                var p = PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "testsdata", "dotyoulogs", _folder);
                return isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
            }
        }

        [OneTimeSetUp]
        public void RunBeforeAnyTests(bool startWebserver = true)
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            this.DeleteData();
            this.DeleteLogs();

            _registry = new DevelopmentIdentityContextRegistry(TestDataPath, TempDataPath);
            _registry.Initialize();

            if (startWebserver)
            {
                Environment.SetEnvironmentVariable("Host__RegistryServerUri", "https://r.youver.se:9443");
                Environment.SetEnvironmentVariable("Host__TenantDataRootPath", TestDataPath);
                Environment.SetEnvironmentVariable("Host__TempTenantDataRootPath", TempDataPath);
                Environment.SetEnvironmentVariable("Host__UseLocalCertificateRegistry", "true");
                Environment.SetEnvironmentVariable("Quartz__EnableQuartzBackgroundService", "false");
                Environment.SetEnvironmentVariable("Quartz__BackgroundJobStartDelaySeconds", "10");
                Environment.SetEnvironmentVariable("Logging__LogFilePath", TempDataPath);

                _webserver = Program.CreateHostBuilder(Array.Empty<string>()).Build();
                _webserver.Start();

                foreach (var identity in TestIdentities.All)
                {
                    this.SetupOwnerAccount(identity).GetAwaiter().GetResult();
                }
            }
        }

        public void DeleteData()
        {
            if (Directory.Exists(TestDataPath))
            {
                Console.WriteLine($"Removing data in [{TestDataPath}]");
                Directory.Delete(TestDataPath, true);
            }

            Directory.CreateDirectory(TestDataPath);

            if (Directory.Exists(TempDataPath))
            {
                Console.WriteLine($"Removing data in [{TempDataPath}]");
                Directory.Delete(TempDataPath, true);
            }

            Directory.CreateDirectory(TempDataPath);
        }

        public void DeleteLogs()
        {
            if (Directory.Exists(LogFilePath))
            {
                Console.WriteLine($"Removing data in [{LogFilePath}]");
                Directory.Delete(LogFilePath, true);
            }

            Directory.CreateDirectory(LogFilePath);
        }

        [OneTimeTearDown]
        public void RunAfterAnyTests()
        {
            if (null != _webserver)
            {
                System.Threading.Thread.Sleep(2000);
                _webserver.StopAsync();
                _webserver.Dispose();
            }
        }

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

        public async Task<(ClientAuthenticationToken, SensitiveByteArray)> LoginToOwnerConsole(string identity, string password)
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

        private async Task SetupOwnerAccount(DotYouIdentity identity)
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

        public HttpClient CreateOwnerApiHttpClient(DotYouIdentity identity, out SensitiveByteArray sharedSecret, Guid? appId = null)
        {
            var token = GetOwnerAuthContext(identity).ConfigureAwait(false).GetAwaiter().GetResult();
            var client = CreateOwnerApiHttpClient(identity, token.AuthenticationResult, appId);
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

        /// <summary>
        /// Creates an http client that has a cookie jar but no authentication tokens.  This is useful for testing token exchanges.
        /// </summary>
        /// <returns></returns>
        public HttpClient CreateAnonymousApiHttpClient(DotYouIdentity identity)
        {
            var cookieJar = new CookieContainer();
            HttpMessageHandler handler = new HttpClientHandler()
            {
                CookieContainer = cookieJar
            };

            HttpClient client = new(handler);
            client.Timeout = TimeSpan.FromMinutes(15);

            client.BaseAddress = new Uri($"https://{identity}");
            return client;
        }

        /// <summary>
        /// Creates a client for use with the app API (/api/apps/v1/...)
        /// </summary>
        public HttpClient CreateAppApiHttpClient(DotYouIdentity identity, ClientAuthenticationToken token)
        {
            var cookieJar = new CookieContainer();
            cookieJar.Add(new Cookie(AppAuthConstants.ClientAuthTokenCookieName, token.ToString(), null, identity));
            HttpMessageHandler handler = new HttpClientHandler()
            {
                CookieContainer = cookieJar
            };

            HttpClient client = new(handler);
            client.Timeout = TimeSpan.FromMinutes(15);

            client.BaseAddress = new Uri($"https://{identity}");
            return client;
        }

        public HttpClient CreateAppApiHttpClient(TestSampleAppContext appTestContext)
        {
            return this.CreateAppApiHttpClient(appTestContext.Identity, appTestContext.ClientAuthenticationToken);
        }

        public Task OutputRequestInfo<T>(ApiResponse<T> response)
        {
            if (null == response.RequestMessage || null == response.RequestMessage.RequestUri)
            {
                return Task.CompletedTask;
            }

            ConsoleColor prev = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Uri -> {response.RequestMessage.RequestUri}");

            string content = "No Content";
            // if (response.RequestMessage.Content != null)
            // {
            //     content = await response.RequestMessage.Content.ReadAsStringAsync();
            // }

            Console.WriteLine($"Content ->\n {content}");
            Console.ForegroundColor = prev;

            return Task.CompletedTask;
        }

        public async Task<AppRegistrationResponse> AddApp(DotYouIdentity identity, Guid appId, Guid appDriveAlias, bool createDrive = false, bool canManageConnections = false)
        {
            var permissionSet = new PermissionSet();
            if (canManageConnections)
            {
                permissionSet.Permissions.Add(SystemApi.CircleNetwork, (int) CircleNetworkPermissions.Manage);
                permissionSet.Permissions.Add(SystemApi.CircleNetworkRequests, (int) CircleNetworkRequestPermissions.Manage);
            }

            using (var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret))
            {
                var driveType = Guid.NewGuid();
                var svc = RestService.For<IAppRegistrationClient>(client);
                var request = new AppRegistrationRequest
                {
                    Name = $"Test_{appId}",
                    ApplicationId = appId,
                    CreateDrive = createDrive,
                    PermissionSet = permissionSet,
                    DefaultDrivePublicId = appDriveAlias,
                    DriveMetadata = "{data:'test metadata'}",
                    DriveName = $"Test Drive name with type {driveType}",
                    DriveType = driveType
                };

                var response = await svc.RegisterApp(request);

                Assert.IsTrue(response.IsSuccessStatusCode);
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
            Guid driveAlias = Guid.NewGuid();
            return await this.SetupTestSampleApp(appId, identity, false, driveAlias);
        }

        public async Task<TestSampleAppContext> SetupTestSampleApp(Guid appId, DotYouIdentity identity, bool canManageConnections = false, Guid appDriveAlias = default)
        {
            //TODO: we might need to let the callers pass this in at some point for testing

            //note; this is intentionally not global

            this.AddApp(identity, appId, appDriveAlias, true, canManageConnections).GetAwaiter().GetResult();

            var (authResult, sharedSecret) = this.AddAppClient(identity, appId).GetAwaiter().GetResult();
            return new TestSampleAppContext()
            {
                Identity = identity,
                AppId = appId,
                ClientAuthenticationToken = authResult,
                AppSharedSecretKey = sharedSecret,
                DriveAlias = appDriveAlias
            };
        }

        public async Task<UploadTestUtilsContext> Upload(DotYouIdentity identity, TransitTestUtilsOptions options = null)
        {
            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var driveAlias = Guid.NewGuid();
            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    DriveAlias = driveAlias,
                    OverwriteFileId = null,
                    ExpiresTimestamp = null
                },
                TransitOptions = null
            };

            var fileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                AppData = new()
                {
                    ContentIsComplete = true,
                    JsonContent = JsonConvert.SerializeObject(new {message = "We're going to the beach; this is encrypted by the app"})
                }
            };

            return (UploadTestUtilsContext) await TransferFile(identity, instructionSet, fileMetadata, options ?? TransitTestUtilsOptions.Default);
        }

        public async Task<UploadTestUtilsContext> Upload(DotYouIdentity identity, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options = null)
        {
            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            Guid appDriveAlias = Guid.Parse("99888555-0000-0000-0000-000000004445");
            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    DriveAlias = appDriveAlias,
                    OverwriteFileId = null,
                    ExpiresTimestamp = null
                },
                TransitOptions = null
            };

            return (UploadTestUtilsContext) await TransferFile(identity, instructionSet, fileMetadata, options ?? TransitTestUtilsOptions.Default);
        }

        /// <summary>
        /// Transfers a file using default file metadata
        /// </summary>
        /// <returns></returns>
        public async Task<TransitTestUtilsContext> TransferFile(DotYouIdentity sender, List<string> recipients, TransitTestUtilsOptions options = null)
        {
            var transferIv = ByteArrayUtil.GetRndByteArray(16);
            var driveAlias = Guid.NewGuid();

            var instructionSet = new UploadInstructionSet()
            {
                TransferIv = transferIv,
                StorageOptions = new StorageOptions()
                {
                    DriveAlias = driveAlias,
                    OverwriteFileId = null,
                    ExpiresTimestamp = null,
                },

                TransitOptions = new TransitOptions()
                {
                    Recipients = recipients
                }
            };

            List<Guid> tags = null;
            if (options?.AppDataCategoryId != null)
            {
                tags = new List<Guid>() {options.AppDataCategoryId};
            }

            var fileMetadata = new UploadFileMetadata()
            {
                ContentType = "application/json",
                PayloadIsEncrypted = true,
                AppData = new()
                {
                    Tags = tags,
                    ContentIsComplete = true,
                    JsonContent = options?.AppDataJsonContent ?? JsonConvert.SerializeObject(new {message = "We're going to the beach; this is encrypted by the app"})
                }
            };

            var o = options ?? TransitTestUtilsOptions.Default;

            var result = await TransferFile(sender, instructionSet, fileMetadata, o);

            if (o.DisconnectIdentitiesAfterTransfer)
            {
                foreach (var recipient in recipients)
                {
                    await this.DisconnectIdentities(sender, (DotYouIdentity) recipient);
                }
            }

            return result;
        }

        public async Task DisconnectIdentities(DotYouIdentity dotYouId1, DotYouIdentity dotYouId2)
        {
            using (var client = this.CreateOwnerApiHttpClient(dotYouId1))
            {
                var disconnectResponse = await RestService.For<ICircleNetworkConnectionsClient>(client).Disconnect(dotYouId2);
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, TestIdentities.Samwise, ConnectionStatus.None);
            }

            using (var client = this.CreateOwnerApiHttpClient(dotYouId2))
            {
                var disconnectResponse = await RestService.For<ICircleNetworkConnectionsClient>(client).Disconnect(dotYouId1);
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, TestIdentities.Frodo, ConnectionStatus.None);
            }
        }

        private async Task AssertConnectionStatus(HttpClient client, string dotYouId, ConnectionStatus expected)
        {
            var svc = RestService.For<ICircleNetworkConnectionsClient>(client);
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
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);

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
                var svc = RestService.For<ICircleNetworkRequestsClient>(client);
                var acceptResponse = await svc.AcceptConnectionRequest(sender);
                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Accept Connection request failed with status code [{acceptResponse.StatusCode}]");
            }
        }


        /// 
        private async Task<TransitTestUtilsContext> TransferFile(DotYouIdentity identity, UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options)
        {
            //TODO: so f'kin hacky.
            if (options.UseOwnerContext)
            {
                return await TransferFileAsOwner(identity, instructionSet, fileMetadata, options);
            }

            return await TransferFileAsApp(identity, instructionSet, fileMetadata, options);
        }

        private async Task<TransitTestUtilsContext> TransferFileAsApp(DotYouIdentity sender, UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options)
        {
            var recipients = instructionSet.TransitOptions?.Recipients ?? new List<string>();

            if (options.ProcessTransitBox & (recipients.Count == 0 || options.ProcessOutbox == false))
            {
                throw new Exception("Options not valid.  There must be at least one recipient and ProcessOutbox must be true when ProcessTransitBox is set to true");
            }

            var appId = Guid.NewGuid();
            var testAppContext = await this.SetupTestSampleApp(appId, sender, false, instructionSet.StorageOptions.DriveAlias);

            //Setup the app on all recipient DIs
            var recipientContexts = new Dictionary<DotYouIdentity, TestSampleAppContext>();
            foreach (var r in instructionSet.TransitOptions?.Recipients ?? new List<string>())
            {
                var recipient = (DotYouIdentity) r;
                var ctx = await this.SetupTestSampleApp(testAppContext.AppId, recipient, false, testAppContext.DriveAlias);
                recipientContexts.Add(recipient, ctx);

                await this.CreateConnection(sender, recipient);
            }

            //make a connection request to the recipients

            var keyHeader = KeyHeader.NewRandom16();
            var transferIv = instructionSet.TransferIv;

            var bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(instructionSet));
            var instructionStream = new MemoryStream(bytes);

            var appSharedSecretKey = testAppContext.AppSharedSecretKey.ToSensitiveByteArray();
            fileMetadata.PayloadIsEncrypted = true;
            var descriptor = new UploadFileDescriptor()
            {
                EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, transferIv, ref appSharedSecretKey),
                FileMetadata = fileMetadata
            };

            var fileDescriptorCipher = Utils.JsonEncryptAes(descriptor, transferIv, ref appSharedSecretKey);

            var payloadData = options?.PayloadData ?? "{payload:true, image:'b64 data'}";
            var payloadCipher = keyHeader.GetEncryptedStreamAes(payloadData);

            using (var client = this.CreateAppApiHttpClient(sender, testAppContext.ClientAuthenticationToken))
            {
                var transitSvc = RestService.For<ITransitTestHttpClient>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(transferResult.File.DriveAlias, Is.Not.EqualTo(Guid.Empty));

                if (instructionSet.TransitOptions?.Recipients != null)
                {
                    Assert.IsTrue(transferResult.RecipientStatus.Count == instructionSet.TransitOptions?.Recipients.Count, "expected recipient count does not match");

                    foreach (var recipient in instructionSet.TransitOptions?.Recipients)
                    {
                        Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");
                        Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.TransferKeyCreated, $"transfer key not created for {recipient}");
                    }
                }

                if (options is {ProcessOutbox: true})
                {
                    await transitSvc.ProcessOutbox();
                }


                if (options is {ProcessTransitBox: true})
                {
                    //wait for process outbox to run
                    Task.Delay(2000).Wait();

                    foreach (var rCtx in recipientContexts)
                    {
                        using (var rClient = CreateAppApiHttpClient(rCtx.Key, rCtx.Value.ClientAuthenticationToken))
                        {
                            var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
                            await transitAppSvc.ProcessTransfers();
                        }
                    }
                }
            }

            keyHeader.AesKey.Wipe();

            return new TransitTestUtilsContext()
            {
                AppId = testAppContext.AppId,
                AuthenticationResult = testAppContext.ClientAuthenticationToken,
                AppSharedSecretKey = appSharedSecretKey,
                InstructionSet = instructionSet,
                FileMetadata = fileMetadata,
                RecipientContexts = recipientContexts,
                PayloadData = payloadData,
                TestAppContext = testAppContext
            };
        }

        private async Task<TransitTestUtilsContext> TransferFileAsOwner(DotYouIdentity sender, UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata, TransitTestUtilsOptions options)
        {
            var recipients = instructionSet.TransitOptions?.Recipients ?? new List<string>();

            if (options.ProcessTransitBox & (recipients.Count == 0 || options.ProcessOutbox == false))
            {
                throw new Exception("Options not valid.  There must be at least one recipient and ProcessOutbox must be true when ProcessTransitBox is set to true");
            }

            Guid appId = Guid.NewGuid();
            var testAppContext = await this.SetupTestSampleApp(appId, sender, false, instructionSet.StorageOptions.DriveAlias);

            //Setup the app on all recipient DIs
            var recipientContexts = new Dictionary<DotYouIdentity, TestSampleAppContext>();
            foreach (var r in instructionSet.TransitOptions?.Recipients ?? new List<string>())
            {
                var recipient = (DotYouIdentity) r;
                var ctx = await this.SetupTestSampleApp(testAppContext.AppId, recipient, false, testAppContext.DriveAlias);
                recipientContexts.Add(recipient, ctx);

                await this.CreateConnection(sender, recipient);
            }

            var payloadData = "{payload:true, image:'b64 data'}";

            using (var client = this.CreateOwnerApiHttpClient(sender, out var sharedSecret, testAppContext.AppId))
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

                var fileDescriptorCipher = Utils.JsonEncryptAes(descriptor, transferIv, ref sharedSecret);

                payloadData = options?.PayloadData ?? payloadData;
                var payloadCipher = keyHeader.GetEncryptedStreamAes(payloadData);

                var transitSvc = RestService.For<ITransitOwnerTestHttpClient>(client);
                var response = await transitSvc.Upload(
                    new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                    new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                    new StreamPart(payloadCipher, "payload.encrypted", "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(transferResult.File.DriveAlias, Is.Not.EqualTo(Guid.Empty));

                if (instructionSet.TransitOptions?.Recipients != null)
                {
                    Assert.IsTrue(transferResult.RecipientStatus.Count == instructionSet.TransitOptions?.Recipients.Count, "expected recipient count does not match");

                    foreach (var recipient in instructionSet.TransitOptions?.Recipients)
                    {
                        Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");
                        Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.TransferKeyCreated, $"transfer key not created for {recipient}");
                    }
                }

                if (options is {ProcessOutbox: true})
                {
                    var resp = await transitSvc.ProcessOutbox();
                    Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
                }


                if (options is {ProcessTransitBox: true})
                {
                    //wait for process outbox to run
                    Task.Delay(2000).Wait();

                    foreach (var rCtx in recipientContexts)
                    {
                        using (var rClient = CreateAppApiHttpClient(rCtx.Key, rCtx.Value.ClientAuthenticationToken))
                        {
                            var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
                            var resp = await transitAppSvc.ProcessTransfers();
                            Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
                        }
                    }
                }

                keyHeader.AesKey.Wipe();
            }

            return new TransitTestUtilsContext()
            {
                AppId = testAppContext.AppId,
                AuthenticationResult = testAppContext.ClientAuthenticationToken,
                AppSharedSecretKey = testAppContext.AppSharedSecretKey.ToSensitiveByteArray(),
                InstructionSet = instructionSet,
                FileMetadata = fileMetadata,
                RecipientContexts = recipientContexts,
                PayloadData = payloadData,
                TestAppContext = testAppContext
            };
        }
    }
}