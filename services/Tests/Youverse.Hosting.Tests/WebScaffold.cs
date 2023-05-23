﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Dns.PowerDns;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Registry;
using Youverse.Core.Services.Registry.Registration;
using Youverse.Core.Storage;
using Youverse.Core.Trie;
using Youverse.Core.Util;
using Youverse.Hosting._dev;
using Youverse.Hosting.Tests.AppAPI.ApiClient;
using Youverse.Hosting.Tests.AppAPI.Utils;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;
using Youverse.Hosting.Tests.OwnerApi.Utils;

namespace Youverse.Hosting.Tests
{
    public class WebScaffold
    {
        private readonly string _folder;

        // private readonly string _password = "EnSøienØ";
        private IHost _webserver;

        private readonly OwnerApiTestUtils _oldOwnerApi;

        // private readonly OwnerApiClient _ownerApiClient;
        private AppApiTestUtils _appApi;
        private ScenarioBootstrapper _scenarios;
        private IIdentityRegistry _registry;
        private readonly string _uniqueSubPath;
        private string _testInstancePrefix;

        public WebScaffold(string folder)
        {
            this._folder = folder;
            this._uniqueSubPath = Guid.NewGuid().ToString();
            _oldOwnerApi = new OwnerApiTestUtils();
        }

        public void RunBeforeAnyTests(bool initializeIdentity = true)
        {
            _testInstancePrefix = Guid.NewGuid().ToString("N");

            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("DOTYOU_ENVIRONMENT", "Development");

            Environment.SetEnvironmentVariable("Development__SslSourcePath", "./https/");
            Environment.SetEnvironmentVariable("Development__PreconfiguredDomains",
                "[\"frodo.dotyou.cloud\",\"sam.dotyou.cloud\", \"merry.dotyou.cloud\",\"pippin.dotyou.cloud\"]");

            Environment.SetEnvironmentVariable("Registry__ProvisioningDomain", "provisioning-dev.youfoundation.id");
            Environment.SetEnvironmentVariable("Registry__ManagedDomains", "[\"dev.dotyou.cloud\"]");
            Environment.SetEnvironmentVariable("Registry__DnsTargetRecordType", "[\"dev.dotyou.cloud\"]");
            Environment.SetEnvironmentVariable("Registry__DnsTargetAddress", "[\"dev.dotyou.cloud\"]");

            Environment.SetEnvironmentVariable("Registry__DnsRecordValues__BareARecords", "[\"127.0.0.1\"]");
            Environment.SetEnvironmentVariable("Registry__DnsRecordValues__WwwCnameTarget", "");
            Environment.SetEnvironmentVariable("Registry__DnsRecordValues__ApiCnameTarget", "");
            Environment.SetEnvironmentVariable("Registry__DnsRecordValues__CApiCnameTarget", "");
            Environment.SetEnvironmentVariable("Registry__DnsRecordValues__FileCnameTarget", "");

            Environment.SetEnvironmentVariable("Host__TenantDataRootPath", TestDataPath);
            Environment.SetEnvironmentVariable("Host__TenantPayloadRootPath", TestPayloadPath);
            Environment.SetEnvironmentVariable("Host__SystemDataRootPath", TestDataPath);
            Environment.SetEnvironmentVariable("Host__IPAddressListenList", "[{ \"Ip\": \"*\",\"HttpsPort\": 443,\"HttpPort\": 80 }]");


            Environment.SetEnvironmentVariable("Logging__LogFilePath", TempDataPath);
            Environment.SetEnvironmentVariable("Logging__Level", "ErrorsOnly"); //Verbose

            Environment.SetEnvironmentVariable("Quartz__EnableQuartzBackgroundService", "false");
            Environment.SetEnvironmentVariable("Quartz__CronBatchSize", "100");
            Environment.SetEnvironmentVariable("Quartz__BackgroundJobStartDelaySeconds", "10");
            Environment.SetEnvironmentVariable("Quartz__CronProcessingInterval", "5");
            Environment.SetEnvironmentVariable("Quartz__EnsureCertificateProcessorIntervalSeconds", "1000");
            Environment.SetEnvironmentVariable("Quartz__ProcessPendingCertificateOrderIntervalInSeconds", "1000");


            Environment.SetEnvironmentVariable("CertificateRenewal__NumberOfCertificateValidationTries", "3");
            Environment.SetEnvironmentVariable("CertificateRenewal__UseCertificateAuthorityProductionServers", "false");
            Environment.SetEnvironmentVariable("CertificateRenewal__CertificateAuthorityAssociatedEmail", "email@nowhere.com");
            Environment.SetEnvironmentVariable("CertificateRenewal__CsrCountryName", "US");
            Environment.SetEnvironmentVariable("CertificateRenewal__CsrState", "CA");
            Environment.SetEnvironmentVariable("CertificateRenewal__CsrLocality", "Berkeley");
            Environment.SetEnvironmentVariable("CertificateRenewal__CsrOrganization", "YF");
            Environment.SetEnvironmentVariable("CertificateRenewal__CsrOrganizationUnit", "Dev");

            CreateData();
            CreateLogs();

            var certificateServiceFactory = CreateCertificateFactoryServiceMock();
            _registry = new FileSystemIdentityRegistry(certificateServiceFactory, TestDataPath, TestPayloadPath);

            var (config, _) = Program.LoadConfig();
            DevEnvironmentSetup.RegisterPreconfiguredDomains(config, _registry);

            _webserver = Program.CreateHostBuilder(Array.Empty<string>()).Build();
            _webserver.Start();

            foreach (var odinId in TestIdentities.All.Keys)
            {
                _oldOwnerApi.SetupOwnerAccount((OdinId)odinId, initializeIdentity).GetAwaiter().GetResult();
            }

            _appApi = new AppApiTestUtils(_oldOwnerApi);
            _scenarios = new ScenarioBootstrapper(_oldOwnerApi, _appApi);
        }

        public void RunAfterAnyTests()
        {
            if (null != _webserver)
            {
                _webserver.StopAsync().GetAwaiter().GetResult();
                _webserver.Dispose();
            }

            this.DeleteData();
            this.DeleteLogs();
        }

        public OwnerApiTestUtils OldOwnerApi =>
            this._oldOwnerApi ?? throw new NullReferenceException("Check if the owner app was initialized in method RunBeforeAnyTests");

        public OwnerApiClient CreateOwnerApiClient(TestIdentity identity)
        {
            return new OwnerApiClient(this._oldOwnerApi, identity);
        }

        public AppApiClient CreateAppClient(TestIdentity identity, Guid appId)
        {
            return new AppApiClient(this._oldOwnerApi, identity, appId);
        }

        public AppApiTestUtils AppApi => this._appApi ?? throw new NullReferenceException("Check if the owner app was initialized in method RunBeforeAnyTests");

        public ScenarioBootstrapper Scenarios => this._scenarios ?? throw new NullReferenceException("");

        /// <summary>
        /// Creates an http client that has a cookie jar but no authentication tokens.  This is useful for testing token exchanges.
        /// </summary>
        /// <returns></returns>
        public HttpClient CreateAnonymousApiHttpClient(OdinId identity, FileSystemType fileSystemType = FileSystemType.Standard)
        {
            var cookieJar = new CookieContainer();
            HttpMessageHandler handler = new HttpClientHandler()
            {
                CookieContainer = cookieJar
            };

            HttpClient client = new(handler);
            client.Timeout = TimeSpan.FromMinutes(15);
            client.DefaultRequestHeaders.Add(DotYouHeaderNames.FileSystemTypeHeader, Enum.GetName(fileSystemType));
            client.BaseAddress = new Uri($"https://{DnsConfigurationSet.PrefixApi}.{identity}");
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


        public YouverseClientErrorCode GetErrorCode(ApiException apiException)
        {
            var problemDetails = DotYouSystemSerializer.Deserialize<ProblemDetails>(apiException.Content!);
            Assert.IsNotNull(problemDetails);
            return (YouverseClientErrorCode)int.Parse(problemDetails.Extensions["errorCode"].ToString() ?? string.Empty);
        }

        public CertificateServiceFactory CreateCertificateFactoryServiceMock()
        {
            var certesAcme = new CertesAcme(
                new Mock<ILogger<CertesAcme>>().Object,
                new Mock<IAcmeHttp01TokenCache>().Object,
                new Mock<IHttpClientFactory>().Object,
                false);

            return new CertificateServiceFactory(
                new Mock<ILogger<CertificateService>>().Object,
                certesAcme,
                new AcmeAccountConfig());
        }

        private void CreateData()
        {
            Directory.CreateDirectory(TestDataPath);
            Directory.CreateDirectory(TestPayloadPath);
            Directory.CreateDirectory(TempDataPath);
        }

        private void DeleteData()
        {
            if (Directory.Exists(TestDataPath))
            {
                Console.WriteLine($"Removing data in [{TestDataPath}]");
                Directory.Delete(TestDataPath, true);
            }

            if (Directory.Exists(TestPayloadPath))
            {
                Console.WriteLine($"Removing data in [{TestPayloadPath}]");
                Directory.Delete(TestPayloadPath, true);
            }

            if (Directory.Exists(TempDataPath))
            {
                Console.WriteLine($"Removing data in [{TempDataPath}]");
                Directory.Delete(TempDataPath, true);
            }
        }

        private void CreateLogs()
        {
            Directory.CreateDirectory(LogFilePath);
        }

        private void DeleteLogs()
        {
            if (Directory.Exists(LogFilePath))
            {
                Console.WriteLine($"Removing data in [{LogFilePath}]");
                Directory.Delete(LogFilePath, true);
            }
        }

        private string TestPayloadPath
        {
            get
            {
                var p = PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "payloads", _uniqueSubPath, "dotyoudata", _folder);
                string x = isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
                return Path.Combine(_testInstancePrefix, x);
            }
        }

        private string TestDataPath
        {
            get
            {
                var p = PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "testsdata", _uniqueSubPath, "dotyoudata", _folder);
                string x = isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
                return Path.Combine(_testInstancePrefix, x);
            }
        }

        private bool isDev => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        private string home => Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("HOMEPATH");

        private string TempDataPath
        {
            get
            {
                var p = PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "tempdata", _uniqueSubPath, "dotyoudata", _folder);
                //return isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
                string x = isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
                return Path.Combine(_testInstancePrefix, x);
            }
        }

        private string LogFilePath
        {
            get
            {
                var p = PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "testsdata", _uniqueSubPath, "dotyoulogs", _folder);
                string x = isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
                return Path.Combine(_testInstancePrefix, x);
            }
        }
    }
}