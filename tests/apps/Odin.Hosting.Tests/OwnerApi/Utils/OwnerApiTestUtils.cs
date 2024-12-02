using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Web;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Cryptography.Data;
using Odin.Core.Cryptography.Login;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.Management;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Incoming;
using Odin.Services.Peer.Incoming.Drive;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing;
using Odin.Services.Registry.Registration;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Hosting.Authentication.Owner;
using Odin.Hosting.Authentication.System;
using Odin.Hosting.Controllers;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.Auth;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.Configuration;
using Odin.Hosting.Tests.AppAPI.Transit;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Connections;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Transit;
using Odin.Hosting.Tests.OwnerApi.Apps;
using Odin.Hosting.Tests.OwnerApi.Authentication;
using Odin.Hosting.Tests.OwnerApi.Configuration;
using Odin.Hosting.Tests.OwnerApi.Drive.Management;
using Odin.Hosting.Tests.OwnerApi.Membership.Circles;
using Odin.Hosting.Tests.OwnerApi.Membership.Connections;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.Utils
{
    public class OwnerApiTestUtils
    {
        public readonly Guid SystemProcessApiKey;
        private readonly string _defaultOwnerPassword = "EnSøienØ";
        private readonly Dictionary<string, OwnerAuthTokenContext> _ownerLoginTokens = new(StringComparer.InvariantCultureIgnoreCase);

        public OwnerApiTestUtils(Guid systemProcessApiKey)
        {
            SystemProcessApiKey = systemProcessApiKey;
        }

        internal static bool ServerCertificateCustomValidation(HttpRequestMessage requestMessage, X509Certificate2 certificate, X509Chain chain,
            SslPolicyErrors sslErrors)
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


        public async Task ForceNewPassword(OdinId identity, string password)
        {
            var handler = new HttpClientHandler();
            var jar = new CookieContainer();
            handler.CookieContainer = jar;
            handler.UseCookies = true;

            // handler.CheckCertificateRevocationList = false;
            handler.ServerCertificateCustomValidationCallback = ServerCertificateCustomValidation;

            // SEB:TODO IHttpClientFactory, but we can't use HttpClientHandler
            using HttpClient authClient = new(handler);
            authClient.BaseAddress = new Uri($"https://{identity.DomainName}:{WebScaffold.HttpsPort}");
            var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

            Console.WriteLine($"forcing new password on {authClient.BaseAddress}");

            // var saltResponse = await svc.GenerateNewSalts();
            // Assert.IsNotNull(saltResponse.Content, "failed to generate new salts");
            // Assert.IsTrue(saltResponse.IsSuccessStatusCode, "failed to generate new salts");
            // var clientSalts = saltResponse.Content;
            // var saltyNonce = new NonceData(clientSalts.SaltPassword64, clientSalts.SaltKek64, clientSalts.PublicPem, clientSalts.CRC)
            // {
            //     Nonce64 = clientSalts.Nonce64
            // };
            // var saltyReply = PasswordDataManager.CalculatePasswordReply(password, saltyNonce);

            var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
            var saltyReply = await CalculatePasswordReply(authClient, password, clientEccFullKey);
            //saltyReply.FirstRunToken = ???

            var newPasswordResponse = await svc.SetNewPassword(saltyReply);
            Assert.IsTrue(newPasswordResponse.IsSuccessStatusCode, "failed forcing a new password");
        }

        /// <summary>
        /// Creates a client password reply when you are setting the owner password
        /// </summary>
        public async Task<PasswordReply> CalculatePasswordReply(HttpClient authClient, string password, EccFullKeyData clientEccFullKey)
        {
            var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

            var saltResponse = await svc.GenerateNewSalts();
            Assert.IsNotNull(saltResponse.Content, "failed to generate new salts");
            Assert.IsTrue(saltResponse.IsSuccessStatusCode, "failed to generate new salts");

            var clientSalts = saltResponse.Content;
            var saltyNonce = new NonceData(clientSalts.SaltPassword64, clientSalts.SaltKek64, clientSalts.PublicJwk, clientSalts.CRC)
            {
                Nonce64 = clientSalts.Nonce64
            };

            var saltyReply = PasswordDataManager.CalculatePasswordReply(password, saltyNonce, clientEccFullKey);

            return saltyReply;
        }

        /// <summary>
        /// Creates a password reply for use when you are authenticating as the owner
        /// </summary>
        public async Task<PasswordReply> CalculateAuthenticationPasswordReply(HttpClient authClient, string password, EccFullKeyData clientEccFullKey)
        {
            var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

            var nonceResponse = await svc.GenerateAuthenticationNonce();
            Assert.IsTrue(nonceResponse.IsSuccessStatusCode, "server failed when getting nonce");
            var clientNonce = nonceResponse.Content;

            var nonce = new NonceData(clientNonce!.SaltPassword64, clientNonce.SaltKek64, clientNonce.PublicJwk, clientNonce.CRC)
            {
                Nonce64 = clientNonce.Nonce64
            };

            var reply = PasswordDataManager.CalculatePasswordReply(password, nonce, clientEccFullKey);
            return reply;
        }

        public HttpClient CreateAnonymousClient(OdinId identity)
        {
            HttpClient authClient = new();
            authClient.BaseAddress = new Uri($"https://{identity.DomainName}:{WebScaffold.HttpsPort}");
            return authClient;
        }

        public async Task<ApiResponse<HttpContent>> ResetPasswordUsingRecoveryKey(OdinId identity, string recoveryKey, string password)
        {
            using var authClient = CreateAnonymousClient(identity);
            var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);
            var saltyReply = await CalculatePasswordReply(authClient, password, clientEccFullKey);

            var svc = RestService.For<IOwnerAuthenticationClient>(authClient);
            var publicKeyResponse = await svc.GetPublicKey(PublicPrivateKeyType.OfflineKey);
            Assert.IsTrue(publicKeyResponse.IsSuccessStatusCode);
            var publicKey = publicKeyResponse.Content;

            var hostPublicKey = new EccPublicKeyData()
            {
                publicKey = publicKey.PublicKey,
                crc32c = publicKey.Crc32,
                expiration = new UnixTimeUtc(publicKey.Expiration)
            };

            var keyHeader = KeyHeader.NewRandom16();

            var transferSharedSecret = clientEccFullKey.GetEcdhSharedSecret(EccKeyListManagement.zeroSensitiveKey, hostPublicKey, saltyReply.Nonce64.FromBase64());

            var encryptedRecoveryKey = new EccEncryptedPayload()
            {
                //Note: i exclude the key type here because the methods that receive
                //this must decide the encryption they expect
                RemotePublicKeyJwk = clientEccFullKey.PublicKeyJwk(),
                Salt = saltyReply.Nonce64.FromBase64(),
                Iv = saltyReply.Nonce64.FromBase64(),
                EncryptionPublicKeyCrc32 = hostPublicKey.crc32c,
                EncryptedData = AesCbc.Encrypt(recoveryKey.ToUtf8ByteArray(), transferSharedSecret, saltyReply.Nonce64.FromBase64())
            };

            var resetRequest = new ResetPasswordUsingRecoveryKeyRequest()
            {
                EncryptedRecoveryKey = encryptedRecoveryKey,
                PasswordReply = saltyReply // WTH ? TODO SECURITY -> These are the secrets, right? We can't transfer them
            };

            return await svc.ResetPasswordUsingRecoveryKey(resetRequest);
        }

        public async Task<(ClientAuthenticationToken cat, SensitiveByteArray sharedSecret)> LoginToOwnerConsole(OdinId identity, string password, EccFullKeyData clientEccFullKey)
        {
            var handler = new HttpClientHandler();
            var jar = new CookieContainer();
            handler.CookieContainer = jar;
            handler.UseCookies = true;

            // SEB:TODO IHttpClientFactory, but we can't use HttpClientHandler
            using HttpClient authClient = new(handler);
            authClient.BaseAddress = new Uri($"https://{identity.DomainName}:{WebScaffold.HttpsPort}");
            var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

            var uri = new Uri($"https://{identity.DomainName}:{WebScaffold.HttpsPort}");

            Console.WriteLine($"authenticating to {uri}");
            // var nonceResponse = await svc.GenerateAuthenticationNonce();
            // Assert.IsTrue(nonceResponse.IsSuccessStatusCode, "server failed when getting nonce");
            // var clientNonce = nonceResponse.Content;
            //
            // //HACK: need to refactor types and drop the client nonce package
            // var nonce = new NonceData(clientNonce!.SaltPassword64, clientNonce.SaltKek64, clientNonce.PublicPem, clientNonce.CRC)
            // {
            //     Nonce64 = clientNonce.Nonce64
            // };
            // var reply = PasswordDataManager.CalculatePasswordReply(password, nonce);

            var reply = await this.CalculateAuthenticationPasswordReply(authClient, password, clientEccFullKey);
            var response = await svc.Authenticate(reply);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to authenticate {identity.DomainName}");
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

        public OwnerAuthTokenContext GetOwnerAuthContext(OdinId identity)
        {
            if (_ownerLoginTokens.TryGetValue(identity, out var context))
            {
                return context;
            }

            throw new Exception($"No token found for {identity}");
        }

        public async Task SetupOwnerAccount(OdinId identity, bool initializeIdentity, string password = null)
        {
            var pwd = password ?? this._defaultOwnerPassword;
            await this.ForceNewPassword(identity, pwd);
            var clientEccFullKey = new EccFullKeyData(EccKeyListManagement.zeroSensitiveKey, EccKeySize.P384, 1);

            var (result, sharedSecret) = await this.LoginToOwnerConsole(identity, pwd, clientEccFullKey);

            var context = new OwnerAuthTokenContext()
            {
                AuthenticationResult = result,
                SharedSecret = sharedSecret
            };

            _ownerLoginTokens.Add(identity, context);

            if (initializeIdentity)
            {
                var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
                {
                    var svc = RefitCreator.RestServiceFor<IRefitOwnerConfiguration>(client, ownerSharedSecret);
                    var setupConfig = new InitialSetupRequest();
                    await svc.InitializeIdentity(setupConfig);
                }
            }
        }

        public HttpClient CreateOwnerApiHttpClient(TestIdentity identity, out SensitiveByteArray sharedSecret,
            FileSystemType fileSystemType = FileSystemType.Standard)
        {
            return this.CreateOwnerApiHttpClient(identity.OdinId, out sharedSecret, fileSystemType);
        }

        public HttpClient CreateOwnerApiHttpClient(OdinId identity, out SensitiveByteArray sharedSecret,
            FileSystemType fileSystemType = FileSystemType.Standard)
        {
            var token = GetOwnerAuthContext(identity);
            var client = CreateOwnerApiHttpClient(identity, token.AuthenticationResult, token.SharedSecret, fileSystemType);
            sharedSecret = token.SharedSecret;
            return client;
        }

        public HttpClient CreateOwnerApiHttpClient(OdinId identity, ClientAuthenticationToken token, SensitiveByteArray sharedSecret,
            FileSystemType fileSystemType)
        {
            var client = WebScaffold.CreateHttpClient<OwnerApiTestUtils>();

            //
            // SEB:NOTE below is a hack to make SharedSecretGetRequestHandler work without instance data.
            // DO NOT do this in production code!
            //
            {
                var cookieValue = $"{OwnerAuthConstants.CookieName}={token}";
                client.DefaultRequestHeaders.Add("Cookie", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-COOKIE", cookieValue);
                client.DefaultRequestHeaders.Add("X-HACK-SHARED-SECRET", Convert.ToBase64String(sharedSecret.GetKey()));
            }

            client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(typeof(FileSystemType), fileSystemType));
            client.Timeout = TimeSpan.FromMinutes(15);

            client.BaseAddress = new Uri($"https://{identity}:{WebScaffold.HttpsPort}");
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
            PermissionSetGrantRequest circleMemberGrantRequest = null,
            string appCorsHostName = null,
            bool canUseTransit = true)
        {
            var keys = new List<int>();

            if (canReadConnections)
            {
                keys.Add(PermissionKeys.ReadConnections);
                keys.Add(PermissionKeys.ReadConnectionRequests);
            }

            if (canUseTransit)
            {
                keys.Add(PermissionKeys.UseTransitWrite);
            }

            PermissionSet permissionSet = new PermissionSet(keys.ToArray());


            var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
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

                var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);

                var request = new AppRegistrationRequest
                {
                    Name = $"Test_{appId}",
                    AppId = appId,
                    PermissionSet = permissionSet,
                    Drives = drives,
                    AuthorizedCircles = authorizedCircles,
                    CircleMemberPermissionGrant = circleMemberGrantRequest,
                    CorsHostName = appCorsHostName
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
            var rsa = new RsaFullKeyData(RsaKeyListManagement.zeroSensitiveKey, 1); // TODO

            var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);

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
                var decryptedData = rsa.Decrypt(RsaKeyListManagement.zeroSensitiveKey, reply.Data); // TODO

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
            var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);

                await svc.RevokeApp(new GetAppRequest() { AppId = appId });
            }
        }

        public async Task UpdateAppAuthorizedCircles(OdinId identity, Guid appId, List<Guid> authorizedCircles, PermissionSetGrantRequest grant)
        {
            var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);

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
            var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerAppRegistration>(client, ownerSharedSecret);

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
            PermissionSetGrantRequest circleMemberGrantRequest = null,
            string appCorsHostName = null,
            bool canUseTransit = true)
        {
            if (null == targetDrive)
            {
                targetDrive = new TargetDrive()
                {
                    Alias = Guid.NewGuid(),
                    Type = Guid.NewGuid()
                };
            }

            await this.AddAppWithAllDrivePermissions(identity.OdinId, appId, targetDrive, true, canReadConnections, driveAllowAnonymousReads, ownerOnlyDrive,
                authorizedCircles, circleMemberGrantRequest, appCorsHostName: appCorsHostName, canUseTransit);

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

        public async Task DisconnectIdentities(OdinId odinId1, OdinId odinId2)
        {
            var client = this.CreateOwnerApiHttpClient(odinId1, out var ownerSharedSecret);
            {
                var disconnectResponse = await RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret)
                    .Disconnect(new OdinIdRequest() { OdinId = odinId2 });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, odinId2, ConnectionStatus.None);
            }

            client = this.CreateOwnerApiHttpClient(odinId2, out ownerSharedSecret);
            {
                var disconnectResponse = await RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret)
                    .Disconnect(new OdinIdRequest() { OdinId = odinId1 });
                Assert.IsTrue(disconnectResponse.IsSuccessStatusCode && disconnectResponse.Content, "failed to disconnect");
                await AssertConnectionStatus(client, ownerSharedSecret, odinId1, ConnectionStatus.None);
            }
        }
        
        public async Task WaitForEmptyOutbox(OdinId sender, TargetDrive targetDrive)
        {
            var c = new TransitApiClient(this, TestIdentities.All[sender]);
            await c.WaitForEmptyOutbox(targetDrive);
        }

        private async Task AssertConnectionStatus(HttpClient client, SensitiveByteArray ownerSharedSecret, string odinId, ConnectionStatus expected)
        {
            var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkConnections>(client, ownerSharedSecret);
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
            var client = this.CreateOwnerApiHttpClient(sender, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

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
            client = this.CreateOwnerApiHttpClient(recipient, out ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleNetworkRequests>(client, ownerSharedSecret);

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

        public async Task CreateDrive(OdinId identity, TargetDrive targetDrive, string name, string metadata, bool allowAnonymousReads, bool ownerOnly = false)
        {
            var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
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
                Assert.NotNull(page.Results.SingleOrDefault(drive =>
                    drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type));
            }
        }

        public async Task EnsureDriveExists(OdinId identity, TargetDrive targetDrive, bool allowAnonymousReads)
        {
            var client = this.CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
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

        public async Task<UploadTestUtilsContext> UploadFile(OdinId identity, UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata,
            string payloadData,
            bool encryptPayload = true, ThumbnailContent thumbnail = null, KeyHeader keyHeader = null, FileSystemType fileSystemType = FileSystemType.Standard)
        {
            Assert.IsNull(instructionSet.TransitOptions?.Recipients, "This method will not send transfers; please ensure recipients are null");

            await this.EnsureDriveExists(identity, instructionSet.StorageOptions.Drive, false);

            var client = this.CreateOwnerApiHttpClient(identity, out var sharedSecret, fileSystemType);
            {
                keyHeader = keyHeader ?? KeyHeader.NewRandom16();
                var instructionStream = new MemoryStream(OdinSystemSerializer.Serialize(instructionSet).ToUtf8ByteArray());

                fileMetadata.IsEncrypted = encryptPayload;
                var descriptor = new UploadFileDescriptor()
                {
                    EncryptedKeyHeader = EncryptedKeyHeader.EncryptKeyHeaderAes(keyHeader, instructionSet.TransferIv, ref sharedSecret),
                    FileMetadata = fileMetadata
                };

                var fileDescriptorCipher = TestUtils.JsonEncryptAes(descriptor, instructionSet.TransferIv, ref sharedSecret);

                MemoryStream payloadCipher = new MemoryStream(payloadData.ToUtf8ByteArray());
                MemoryStream payloadCipher2 = new MemoryStream(payloadData.ToUtf8ByteArray());

                KeyHeader payloadKeyHeader = null;
                if (encryptPayload)
                {
                    payloadKeyHeader = new KeyHeader()
                    {
                        AesKey = keyHeader.AesKey,
                        Iv = instructionSet.Manifest.PayloadDescriptors.Single().Iv
                    };

                    var payloadCipherBytes = payloadKeyHeader.EncryptDataAes(payloadData.ToUtf8ByteArray());
                    payloadCipher = new MemoryStream(payloadCipherBytes);
                    payloadCipher2 = new MemoryStream(payloadCipherBytes);
                }

                var transitSvc = RestService.For<IDriveTestHttpClientForOwner>(client);

                ApiResponse<UploadResult> response;
                if (thumbnail == null)
                {
                    response = await transitSvc.Upload(
                        new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                        new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                        new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));
                }
                else
                {
                    var thumbnailCipherBytes = payloadKeyHeader == null
                        ? new MemoryStream(thumbnail.Content)
                        : payloadKeyHeader.EncryptDataAesAsStream(thumbnail.Content);

                    response = await transitSvc.Upload(
                        new StreamPart(instructionStream, "instructionSet.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Instructions)),
                        new StreamPart(fileDescriptorCipher, "fileDescriptor.encrypted", "application/json", Enum.GetName(MultipartUploadParts.Metadata)),
                        new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)),
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
                    UploadResult = transferResult,
                    PayloadCipher = payloadCipher2.ToByteArray(),
                    FileSystemType = fileSystemType
                };
            }
        }

        private async Task<UploadTestUtilsContext> TransferFile(OdinId sender, UploadInstructionSet instructionSet, UploadFileMetadata fileMetadata,
            TransitTestUtilsOptions options)
        {
            var recipients = instructionSet.TransitOptions?.Recipients ?? new List<string>();

            if (options.ProcessInboxBox & (recipients.Count == 0 || options.ProcessOutbox == false))
            {
                throw new Exception(
                    "Options not valid.  There must be at least one recipient and ProcessOutbox must be true when ProcessTransitBox is set to true");
            }

            var targetDrive = instructionSet.StorageOptions.Drive;

            //Feature added much later in schedule but it means we don't have to thread sleep in our unit tests
            if (options.ProcessOutbox && instructionSet.TransitOptions != null)
            {
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

            var client = this.CreateOwnerApiHttpClient(sender, out var sharedSecret);
            {
                var keyHeader = KeyHeader.NewRandom16();
                var transferIv = instructionSet.TransferIv;

                var bytes = System.Text.Encoding.UTF8.GetBytes(OdinSystemSerializer.Serialize(instructionSet));
                var instructionStream = new MemoryStream(bytes);

                fileMetadata.IsEncrypted = options.EncryptPayload;
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
                    new StreamPart(payloadCipher, WebScaffold.PAYLOAD_KEY, "application/x-binary", Enum.GetName(MultipartUploadParts.Payload)));

                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(response.Content, Is.Not.Null);
                var transferResult = response.Content;

                Assert.That(transferResult.File, Is.Not.Null);
                Assert.That(transferResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
                Assert.That(transferResult.File.TargetDrive, Is.Not.EqualTo(Guid.Empty));

                int outboxBatchSize = 1;
                if (instructionSet.TransitOptions?.Recipients?.Any() ?? false)
                {
                    Assert.IsTrue(transferResult.RecipientStatus.Count == instructionSet.TransitOptions?.Recipients.Count,
                        "expected recipient count does not match");

                    foreach (var recipient in instructionSet.TransitOptions?.Recipients)
                    {
                        Assert.IsTrue(transferResult.RecipientStatus.ContainsKey(recipient), $"Could not find matching recipient {recipient}");
                        Assert.IsTrue(transferResult.RecipientStatus[recipient] == TransferStatus.Enqueued,
                            $"transfer key not created for {recipient}");
                    }

                    outboxBatchSize = transferResult.RecipientStatus.Count;
                }

                // if (options is { ProcessOutbox: true })
                // {
                //     var resp = await transitSvc.ProcessOutbox(outboxBatchSize);
                //     Assert.IsTrue(resp.IsSuccessStatusCode, resp.ReasonPhrase);
                // }

                if (options is { ProcessInboxBox: true })
                {
                    //wait for process outbox to run
                    // Task.Delay(2000).Wait();

                    foreach (var recipient in recipients)
                    {
                        //TODO: this should be a create app http client but it works because the path on ITransitTestAppHttpClient is /apps
                        var rClient = CreateOwnerApiHttpClient((OdinId)recipient, out var _);
                        {
                            var transitAppSvc = RestService.For<ITransitTestAppHttpClient>(rClient);
                            rClient.DefaultRequestHeaders.Add(SystemAuthConstants.Header, SystemProcessApiKey.ToString());

                            var resp = await transitAppSvc.ProcessInbox(new ProcessInboxRequest() { TargetDrive = targetDrive });
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
                    UploadResult = transferResult,
                };
            }
        }

        public async Task<CircleDefinition> CreateCircleWithDrive(OdinId identity, string circleName, IEnumerable<int> permissionKeys, PermissionedDrive drive)
        {
            return await this.CreateCircleWithDrive(identity, circleName, permissionKeys, new List<PermissionedDrive>() { drive });
        }

        public async Task<CircleDefinition> CreateCircleWithDrive(OdinId identity, string circleName, IEnumerable<int> permissionKeys,
            List<PermissionedDrive> drives)
        {
            var client = CreateOwnerApiHttpClient(identity, out var ownerSharedSecret);
            {
                var svc = RefitCreator.RestServiceFor<IRefitOwnerCircleDefinition>(client, ownerSharedSecret);

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