using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web;
using DotYou.IdentityRegistry;
using DotYou.Kernel.Cryptography;
using DotYou.TenantHost.Security;
using DotYou.Types;
using DotYou.Types.Admin;
using DotYou.Types.ApiClient;
using DotYou.Types.Cryptography;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Refit;

namespace DotYou.TenantHost.WebAPI.Tests
{
    public class TestScaffold
    {
        private string _folder;
        private IHost _webserver;
        private Dictionary<string, AuthenticationResult> tokens = new Dictionary<string, AuthenticationResult>(StringComparer.InvariantCultureIgnoreCase);

        IdentityContextRegistry _registry;

        public TestScaffold(string folder)
        {
            this._folder = folder;
        }

        public DotYouIdentity Frodo = (DotYouIdentity)"frodobaggins.me";
        public DotYouIdentity Samwise = (DotYouIdentity)"samwisegamgee.me";

        [OneTimeSetUp]
        public void RunBeforeAnyTests(bool startWebserver = true)
        {
            string testDataPath = Path.Combine(Path.DirectorySeparatorChar.ToString(), @"tmp", "dotyoudata", _folder);
            string logFilePath = Path.Combine(Path.DirectorySeparatorChar.ToString(), @"tmp", "dotyoulogs", _folder);

            if (Directory.Exists(testDataPath))
            {
                Console.WriteLine($"Removing data in [{testDataPath}]");
                Directory.Delete(testDataPath, true);
            }

            if (Directory.Exists(logFilePath))
            {
                Console.WriteLine($"Removing data in [{logFilePath}]");
                Directory.Delete(logFilePath, true);
            }

            Directory.CreateDirectory(testDataPath);
            Directory.CreateDirectory(logFilePath);

            _registry = new IdentityContextRegistry(testDataPath);
            _registry.Initialize();

            if (startWebserver)
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
                var args = new string[2];
                args[0] = testDataPath;
                args[1] = logFilePath;
                _webserver = Program.CreateHostBuilder(args).Build();
                _webserver.Start();
            }
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
            

            Assert.IsTrue(AuthenticationResult.TryParse(tokenCookie, out var result), "invalid authentication cookie returned");

            var newToken = result.Token;
            Assert.IsTrue(newToken != Guid.Empty);
            Assert.IsTrue(result.Token2 != Guid.Empty);

            tokens.Add(identity, result);
            return result.ToString();
        }

        public HttpClient CreateHttpClient(DotYouIdentity identity)
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
            //client.DefaultRequestHeaders.Add(DotYouHeaderNames.AuthToken, token.ToString());

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