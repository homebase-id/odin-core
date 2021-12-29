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
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Controllers.Owner;
using Youverse.Hosting.Tests.ApiClient;
using Youverse.Hosting.Tests.OwnerApi;

namespace Youverse.Hosting.Tests
{
    public class AppTestScaffold
    {
        private string _folder;
        private IHost _webserver;
        private Dictionary<string, DotYouAuthenticationResult> tokens = new (StringComparer.InvariantCultureIgnoreCase);

        DevelopmentIdentityContextRegistry _registry;

        public AppTestScaffold(string folder)
        {
            this._folder = folder;
        }

        public Guid ApplicationId = Guid.Parse("99950012-0012-5555-5555-777777777777");
        public byte[] DeviceUid = Guid.Parse("00000001-0000-3333-3333-888888888888").ToByteArray();

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
        
        private async Task<DotYouAuthenticationResult> EnsureAuthToken(DotYouIdentity identity)
        {
            if (tokens.TryGetValue(identity, out var authResult))
            {
                return authResult;
            }

            const string password = "EnSøienØ";
            await this.ForceNewPassword(identity, password);

            var result = await this.LoginToOwnerConsole(identity, password);
            tokens.Add(identity, result);
            return result;
        }

        public HttpClient CreateHttpClient(DotYouIdentity identity)
        {
            var token = EnsureAuthToken(identity).ConfigureAwait(false).GetAwaiter().GetResult();
            var client = CreateHttpClient(identity, token);

            return client;
        }

        public HttpClient CreateHttpClient(DotYouIdentity identity, DotYouAuthenticationResult token)
        {
            Console.WriteLine("CreateHttpClient");
            var cookieJar = new CookieContainer();
            cookieJar.Add(new Cookie(OwnerAuthConstants.CookieName, token.ToString(), null, identity));
            HttpMessageHandler handler = new HttpClientHandler()
            {
                CookieContainer = cookieJar
            };
            HttpClient client = new(handler);
            client.Timeout = TimeSpan.FromMinutes(15);
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