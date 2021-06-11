using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types;
using DotYou.Types.Admin;
using DotYou.Types.ApiClient;
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

        public IdentityContextRegistry Registry
        {
            get => _registry;
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

        private Guid EnsureAuthToken(DotYouIdentity identity)
        {
            if (tokens.TryGetValue(identity, out Guid token))
            {
                return token;
            }

            using HttpClient authClient = new();
            authClient.BaseAddress = new Uri($"https://{identity}");
            var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

            string password = "";
            throw new Exception("TODO: integrate Nonce auth");
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
            var token = EnsureAuthToken(identity);

            HttpClient client = new();
            client.DefaultRequestHeaders.Add(DotYouHeaderNames.AuthToken, token.ToString());
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