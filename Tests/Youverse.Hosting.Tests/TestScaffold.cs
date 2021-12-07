using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Identity;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Registry;
using Youverse.Core.Util;
using Youverse.Hosting.Security;
using Youverse.Hosting.Tests.ApiClient;

namespace Youverse.Hosting.Tests
{
    public class TestScaffold
    {
        private string _folder;
        private IHost _webserver;
        private Dictionary<string, DotYouAuthenticationResult> tokens = new Dictionary<string, DotYouAuthenticationResult>(StringComparer.InvariantCultureIgnoreCase);

        IdentityContextRegistry _registry;

        public TestScaffold(string folder)
        {
            this._folder = folder;
        }

        public DotYouIdentity Frodo = (DotYouIdentity) "frodobaggins.me";
        public DotYouIdentity Samwise = (DotYouIdentity) "samwisegamgee.me";

        public string AppId = "WebApiTests";
        public string DeviceUid = "WebApiTestsDeviceUid";

        public string TestDataPath => PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "testsdata", "dotyoudata", _folder);
        public string TempDataPath => PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "tempdata", "dotyoudata", _folder);
        public string LogFilePath => PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "testsdata", "dotyoulogs", _folder);

        [OneTimeSetUp]
        public void RunBeforeAnyTests(bool startWebserver = true)
        {
            this.DeleteData();
            this.DeleteLogs();

            _registry = new IdentityContextRegistry(TestDataPath, TempDataPath);
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

        private async Task<string> EnsureAuthToken(DotYouIdentity identity)
        {
            const string password = "EnSøienØ";

            if (tokens.TryGetValue(identity, out var authResult))
            {
                return authResult.ToString();
            }

            var handler = new HttpClientHandler();
            var jar = new CookieContainer();
            handler.CookieContainer = jar;
            handler.UseCookies = true;

            using HttpClient authClient = new(handler);
            authClient.DefaultRequestHeaders.Add(DotYouHeaderNames.AppId, AppId);
            authClient.DefaultRequestHeaders.Add(DotYouHeaderNames.DeviceUid, DeviceUid);
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
            Assert.IsTrue(newPasswordResponse.Content?.Success, "failed forcing a new password");

            Console.WriteLine($"authenticating to {authClient.BaseAddress}");
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
            var tokenCookie = HttpUtility.UrlDecode(cookies[DotYouAuthConstants.TokenKey]?.Value);


            Assert.IsTrue(DotYouAuthenticationResult.TryParse(tokenCookie, out var result), "invalid authentication cookie returned");

            var newToken = result.SessionToken;
            Assert.IsTrue(newToken != Guid.Empty);
            Assert.IsTrue(result.ClientHalfKek.IsSet());

            tokens.Add(identity, result);
            return result.ToString();
        }

        public HttpClient CreateHttpClient(DotYouIdentity identity, bool ignoreAuth = false, bool runAsAdminApp = false, Dictionary<string, string> additionalHeaders = null)
            //public HttpClient CreateHttpClient(DotYouIdentity identity)
        {
            Console.WriteLine("CreateHttpClient");
            var token = EnsureAuthToken(identity).ConfigureAwait(false).GetAwaiter().GetResult();
            var cookieJar = new CookieContainer();
            cookieJar.Add(new Cookie(DotYouAuthConstants.TokenKey, token, null, identity));
            HttpMessageHandler handler = new HttpClientHandler()
            {
                CookieContainer = cookieJar
            };
            HttpClient client = new(handler);
            client.Timeout = TimeSpan.FromMinutes(15);
            client.DefaultRequestHeaders.Add(DotYouHeaderNames.AppId, AppId);
            client.DefaultRequestHeaders.Add(DotYouHeaderNames.DeviceUid, DeviceUid);
            
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
    }
}