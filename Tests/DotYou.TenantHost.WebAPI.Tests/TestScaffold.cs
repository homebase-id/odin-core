using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using NUnit.Framework;
using Refit;

namespace DotYou.TenantHost.WebAPI.Tests
{
    public class TestScaffold
    {
        // private IHost webserver;
        private string _folder;
        private IHost webserver;
        IdentityContextRegistry _registry;

        public static DotYouIdentity frodo = (DotYouIdentity) "frodobaggins.me";
        public static DotYouIdentity samwise = (DotYouIdentity) "samwisegamgee.me";

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
                webserver = Program.CreateHostBuilder(args).Build();
                webserver.Start();
            }
        }

        [OneTimeTearDown]
        public void RunAfterAnyTests()
        {
            if (null != webserver)
            {
                System.Threading.Thread.Sleep(2000);
                webserver.StopAsync();
                webserver.Dispose();
            }
        }

        public HttpClient CreateHttpClient(DotYouIdentity identity)
        {
            var samContext = this.Registry.ResolveContext(identity);
            var samCert = samContext.TenantCertificate.LoadCertificateWithPrivateKey();

            HttpClientHandler handler = new();
            handler.ClientCertificates.Add(samCert);
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;

            HttpClient client = new(handler);

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