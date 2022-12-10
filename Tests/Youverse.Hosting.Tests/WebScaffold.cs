using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Identity;
using Youverse.Core.Services.Registry;
using Youverse.Core.Util;
using Youverse.Hosting._dev;
using Youverse.Hosting.Tests.AppAPI.Utils;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests
{
    public class WebScaffold
    {
        private readonly string _folder;
        private readonly string _password = "EnSøienØ";
        private IHost _webserver;
        private readonly OwnerApiTestUtils _ownerApi;
        private AppApiTestUtils _appApi;
        private ScenarioBootstrapper _scenarios;
        private IIdentityRegistry _registry;

        public WebScaffold(string folder)
        {
            this._folder = folder;
            _ownerApi = new OwnerApiTestUtils();
        }

        [OneTimeSetUp]
        public void RunBeforeAnyTests(bool initializeIdentity = true)
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            Environment.SetEnvironmentVariable("Host__ProvisioningDomain", "provisioning-dev.onekin.io");
            Environment.SetEnvironmentVariable("Host__TenantDataRootPath", TestDataPath);
            Environment.SetEnvironmentVariable("Host__UseLocalCertificateRegistry", "true");
            Environment.SetEnvironmentVariable("Quartz__EnableQuartzBackgroundService", "false");
            Environment.SetEnvironmentVariable("Quartz__BackgroundJobStartDelaySeconds", "10");
            Environment.SetEnvironmentVariable("Quartz__ProcessOutboxIntervalSeconds", "5");
            Environment.SetEnvironmentVariable("Quartz__EnsureCertificateProcessorIntervalSeconds", "1000");
            Environment.SetEnvironmentVariable("Quartz__ProcessPendingCertificateOrderIntervalInSeconds", "1000");
            Environment.SetEnvironmentVariable("Logging__LogFilePath", TempDataPath);

            Environment.SetEnvironmentVariable("CertificateRenewal__NumberOfCertificateValidationTries", "3");
            Environment.SetEnvironmentVariable("CertificateRenewal__NumberOfCertificateValidationTries", "false");
            Environment.SetEnvironmentVariable("CertificateRenewal__CertificateAuthorityAssociatedEmail", "email@nowhere.com");
            Environment.SetEnvironmentVariable("CertificateRenewal__CsrCountryName", "US");
            Environment.SetEnvironmentVariable("CertificateRenewal__CsrState", "CA");
            Environment.SetEnvironmentVariable("CertificateRenewal__CsrLocality", "Berkeley");
            Environment.SetEnvironmentVariable("CertificateRenewal__CsrOrganization", "YF");
            Environment.SetEnvironmentVariable("CertificateRenewal__CsrOrganizationUnit", "Dev");
            
            Environment.SetEnvironmentVariable("Development__SslSourcePath", "./https/");
            // Environment.SetEnvironmentVariable("Development__PreconfiguredDomains",);

                // "PreconfiguredDomains": [
                // "frodo.digital",
                // "samwise.gamgee",
                // "merry.onekin.io",
                // "pippin.onekin.io"
                //     ]
            
                
            this.DeleteData();
            this.DeleteLogs();
            
            
            _registry = new FileSystemIdentityRegistry(TestDataPath, null);
            _registry.Initialize();
            var (config, _) = Program.LoadConfig();
            DevEnvironmentSetup.RegisterPreconfiguredDomains(config, _registry);

            _webserver = Program.CreateHostBuilder(Array.Empty<string>()).Build();
            _webserver.Start();

            foreach (var dotYouId in TestIdentities.All.Keys)
            {
                _ownerApi.SetupOwnerAccount((DotYouIdentity)dotYouId, initializeIdentity).GetAwaiter().GetResult();
            }

            _appApi = new AppApiTestUtils(_ownerApi);
            _scenarios = new ScenarioBootstrapper(_ownerApi, _appApi);
        }

        [OneTimeTearDown]
        public void RunAfterAnyTests()
        {
            if (null != _webserver)
            {
                Thread.Sleep(2000);
                _webserver.StopAsync();
                _webserver.Dispose();
            }
        }

        public OwnerApiTestUtils OwnerApi => this._ownerApi ?? throw new NullReferenceException("Check if the owner app was initialized in method RunBeforeAnyTests");

        public AppApiTestUtils AppApi => this._appApi ?? throw new NullReferenceException("Check if the owner app was initialized in method RunBeforeAnyTests");

        public ScenarioBootstrapper Scenarios => this._scenarios ?? throw new NullReferenceException("");

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


        public T RestServiceFor<T>(HttpClient client, byte[] sharedSecret)
        {
            return RefitCreator.RestServiceFor<T>(client, sharedSecret.ToSensitiveByteArray());
        }

        /// <summary>
        /// Creates a Refit service using the shared secret encrypt/decrypt wrapper
        /// </summary>
        public T RestServiceFor<T>(HttpClient client, SensitiveByteArray sharedSecret)
        {
            return RefitCreator.RestServiceFor<T>(client, sharedSecret);
        }

        private void DeleteData()
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

        private void DeleteLogs()
        {
            if (Directory.Exists(LogFilePath))
            {
                Console.WriteLine($"Removing data in [{LogFilePath}]");
                Directory.Delete(LogFilePath, true);
            }

            Directory.CreateDirectory(LogFilePath);
        }

        private string TestDataPath
        {
            get
            {
                var p = PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "testsdata", "dotyoudata", _folder);
                string x = isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
                return x;
            }
        }

        private bool isDev => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        private string home => Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("HOMEPATH");

        private string TempDataPath
        {
            get
            {
                var p = PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "tempdata", "dotyoudata", _folder);
                return isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
            }
        }

        private string LogFilePath
        {
            get
            {
                var p = PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "testsdata", "dotyoulogs", _folder);
                return isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
            }
        }
    }
}