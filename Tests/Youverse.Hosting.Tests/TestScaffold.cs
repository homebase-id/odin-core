﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.AppAuth;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Registry;
using Youverse.Core.Util;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Controllers.Owner.AppManagement;
using Youverse.Hosting.Tests.OwnerApi.Apps;
using Youverse.Hosting.Tests.OwnerApi.Authentication;

namespace Youverse.Hosting.Tests
{
    public class TestScaffold
    {
        private readonly string _folder;
        private IHost _webserver;
        private Dictionary<string, DotYouAuthenticationResult> ownerLoginTokens = new Dictionary<string, DotYouAuthenticationResult>(StringComparer.InvariantCultureIgnoreCase);

        DevelopmentIdentityContextRegistry _registry;

        public TestScaffold(string folder)
        {
            this._folder = folder;
        }

        public readonly byte[] AppSharedSecret = Guid.Empty.ToByteArray();

        public string TestDataPath => PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "testsdata", "dotyoudata", _folder);
        public string TempDataPath => PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "tempdata", "dotyoudata", _folder);
        public string LogFilePath => PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "testsdata", "dotyoulogs", _folder);

        [OneTimeSetUp]
        public void RunBeforeAnyTests(bool startWebserver = true)
        {
            this.DeleteData();
            this.DeleteLogs();

            _registry = new DevelopmentIdentityContextRegistry(TestDataPath, TempDataPath);
            _registry.Initialize();

            if (startWebserver)
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
                Environment.SetEnvironmentVariable("Host__RegistryServerUri", "https://r.youver.se:9443");
                Environment.SetEnvironmentVariable("Host__TenantDataRootPath", TestDataPath);
                Environment.SetEnvironmentVariable("Host__TempTenantDataRootPath", TempDataPath);
                Environment.SetEnvironmentVariable("Host__UseLocalCertificateRegistry", "true");
                Environment.SetEnvironmentVariable("Quartz__EnableQuartzBackgroundService", "false");
                Environment.SetEnvironmentVariable("Quartz__BackgroundJobStartDelaySeconds", "10");
                Environment.SetEnvironmentVariable("Logging__LogFilePath", TempDataPath);

                _webserver = Program.CreateHostBuilder(Array.Empty<string>()).Build();
                _webserver.Start();
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
            var saltyReply = LoginKeyManager.CalculatePasswordReply(password, saltyNonce);

            var newPasswordResponse = await svc.SetNewPassword(saltyReply);
            Assert.IsTrue(newPasswordResponse.IsSuccessStatusCode, "failed forcing a new password");
        }

        public async Task<DotYouAuthenticationResult> LoginToOwnerConsole(string identity, string password)
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
            var nonce = new NonceData(clientNonce.SaltPassword64, clientNonce.SaltKek64, clientNonce.PublicPem, clientNonce.CRC)
            {
                Nonce64 = clientNonce.Nonce64
            };
            var reply = LoginKeyManager.CalculatePasswordReply(password, nonce);
            var response = await svc.Authenticate(reply);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to authenticate {identity}");
            Assert.IsTrue(response.Content, $"Failed to authenticate {identity}");

            var cookies = jar.GetCookies(authClient.BaseAddress);
            var tokenCookie = HttpUtility.UrlDecode(cookies[OwnerAuthConstants.CookieName]?.Value);

            Assert.IsTrue(DotYouAuthenticationResult.TryParse(tokenCookie, out var result), "invalid authentication cookie returned");

            var newToken = result.SessionToken;
            Assert.IsTrue(newToken != Guid.Empty);
            Assert.IsTrue(result.ClientHalfKek.IsSet());
            return result;
        }

        private async Task<DotYouAuthenticationResult> EnsureAuthToken(DotYouIdentity identity)
        {
            if (ownerLoginTokens.TryGetValue(identity, out var authResult))
            {
                return authResult;
            }

            const string password = "EnSøienØ";
            await this.ForceNewPassword(identity, password);

            var result = await this.LoginToOwnerConsole(identity, password);
            ownerLoginTokens.Add(identity, result);
            return result;
        }

        public HttpClient CreateOwnerApiHttpClient(DotYouIdentity identity)
        {
            var token = EnsureAuthToken(identity).ConfigureAwait(false).GetAwaiter().GetResult();
            var client = CreateOwnerApiHttpClient(identity, token);

            return client;
        }

        public HttpClient CreateOwnerApiHttpClient(DotYouIdentity identity, DotYouAuthenticationResult token)
        {
            var cookieJar = new CookieContainer();
            cookieJar.Add(new Cookie(OwnerAuthConstants.CookieName, token.ToString(), null, identity));
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

        public async Task<AppRegistrationResponse> AddSampleApp(DotYouIdentity identity, Guid appId, bool createDrive = false, bool revoke = false)
        {
            using (var client = this.CreateOwnerApiHttpClient(identity))
            {
                var svc = RestService.For<IAppRegistrationClient>(client);
                var request = new AppRegistrationRequest
                {
                    Name = $"Test_{appId}",
                    ApplicationId = appId,
                    CreateDrive = createDrive
                };

                var response = await svc.RegisterApp(request);

                Assert.IsTrue(response.IsSuccessStatusCode);
                var appReg = response.Content;
                Assert.IsNotNull(appReg);

                if (revoke)
                {
                    await svc.RevokeApp(appId);
                }

                var updatedAppResponse = await svc.GetRegisteredApp(appId);
                Assert.That(updatedAppResponse.IsSuccessStatusCode, Is.True);
                Assert.That(updatedAppResponse.Content, Is.Not.Null);

                return updatedAppResponse.Content;
            }
        }

        public async Task<AppDeviceRegistrationResponse> AddAppDevice(DotYouIdentity identity, Guid appId, byte[] deviceUid)
        {
            using (var client = this.CreateOwnerApiHttpClient(identity))
            {
                var svc = RestService.For<IAppRegistrationClient>(client);

                var request = new AppDeviceRegistrationRequest()
                {
                    ApplicationId = appId,
                    DeviceId64 = Convert.ToBase64String(deviceUid),
                    SharedSecret64 = Convert.ToBase64String(this.AppSharedSecret)
                };

                var regResponse = await svc.RegisterAppOnDevice(request);
                Assert.IsTrue(regResponse.IsSuccessStatusCode);
                Assert.IsNotNull(regResponse.Content);

                return regResponse.Content;
            }
        }

        public async Task RevokeSampleApp(DotYouIdentity identity, Guid appId)
        {
            using (var client = this.CreateOwnerApiHttpClient(identity))
            {
                var svc = RestService.For<IAppRegistrationClient>(client);
                await svc.RevokeApp(appId);
            }
        }

        public async Task<Guid> CreateAppSession(DotYouIdentity identity, Guid appId, byte[] deviceUid)
        {
            var appDevice = new AppDevice()
            {
                ApplicationId = appId,
                DeviceUid = deviceUid
            };
            
            using (var ownerClient = this.CreateOwnerApiHttpClient(identity))
            {
                var ownerAuthSvc = RestService.For<IOwnerAuthenticationClient>(ownerClient);
                var authCodeResponse = await ownerAuthSvc.CreateAppSession(appDevice);
                Assert.That(authCodeResponse.IsSuccessStatusCode, Is.True);

                var authCode = authCodeResponse.Content;
                Assert.That(authCode, Is.Not.EqualTo(Guid.Empty));

                return authCode;
            }
        }
        
    }
}