using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
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
        private Dictionary<string, Guid> tokens = new Dictionary<string, Guid>(StringComparer.InvariantCultureIgnoreCase);

        IdentityContextRegistry _registry;

        public TestScaffold(string folder)
        {
            this._folder = folder;
        }

        public DotYouIdentity Frodo = (DotYouIdentity) "frodobaggins.me";
        public DotYouIdentity Samwise = (DotYouIdentity) "samwisegamgee.me";

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
            return "cfb35a93-ed47-7701-f4cb-f2fd38e2ba91%7C544251bc-2de4-5fb6-bc92-a0ec981b6203";
            //might need to decode this to : cfb35a93-ed47-7701-f4cb-f2fd38e2ba91|544251bc-2de4-5fb6-bc92-a0ec981b6203

            // if (tokens.TryGetValue(identity, out Guid token))
            // {
            //     return token;
            // }
            
            // using HttpClient authClient = new();
            // authClient.BaseAddress = new Uri($"https://{identity}");
            // var svc = RestService.For<IOwnerAuthenticationClient>(authClient);
            
            string password = "";
            //
            // var response = svc.Authenticate(password).ConfigureAwait(false).GetAwaiter().GetResult();
            //
            // Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to authenticate {identity}");
            // var result = response.Content;
            // Assert.NotNull(result, "No authentication result returned");
            // var newToken = result.Token;
            // Assert.IsTrue(newToken != Guid.Empty);
            //
            // tokens.Add(identity, newToken);
            // return newToken;
            }

        public HttpClient CreateHttpClient(DotYouIdentity identity)
        {
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