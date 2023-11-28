using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using HttpClientFactoryLite;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives.DriveCore.Storage;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Core.Util;
using Odin.Hosting.Tests.AppAPI.ApiClient;
using Odin.Hosting.Tests.AppAPI.ApiClient.Base;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Refit;

namespace Odin.Hosting.Tests
{
    public class WebScaffold
    {
        /// <summary>
        /// This key is used in transition while both adding multi-payload support AND shifting tests
        /// to use the client api (where each test will support it's own payload key)
        /// </summary>
        public const string PAYLOAD_KEY = "test_key";
        
        // count TIME_WAIT: netstat -p tcp | grep TIME_WAIT | wc -l
        public static readonly HttpClientFactoryLite.HttpClientFactory HttpClientFactory = new();

        private readonly string _folder;
        

        // private readonly string _password = "EnSøienØ";
        private IHost _webserver;

        private readonly OwnerApiTestUtils _oldOwnerApi;

        // private readonly OwnerApiClient _ownerApiClient;
        private AppApiTestUtils _appApi;
        private ScenarioBootstrapper _scenarios;
        private readonly string _uniqueSubPath;
        private string _testInstancePrefix;
        
        public Guid SystemProcessApiKey = Guid.NewGuid();

        public IServiceProvider Services => _webserver.Services;
        
        static WebScaffold()
        {
            HttpClientFactory.Register<OwnerApiTestUtils>(b =>
                b.ConfigurePrimaryHttpMessageHandler(() => new SharedSecretGetRequestHandler
                {
                    UseCookies = false // DO NOT CHANGE!
                }));

            HttpClientFactory.Register<AppApiTestUtils>(b =>
                b.ConfigurePrimaryHttpMessageHandler(() => new SharedSecretGetRequestHandler
                {
                    UseCookies = false // DO NOT CHANGE!
                }));

            HttpClientFactory.Register<AppApiClientBase>(b =>
                b.ConfigurePrimaryHttpMessageHandler(() => new SharedSecretGetRequestHandler
                {
                    UseCookies = false // DO NOT CHANGE!
                }));

            HttpClientFactory.Register("no-cookies-no-redirects", b =>
                b.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false, // DO NOT CHANGE!
                    UseCookies = false // DO NOT CHANGE!
                }));
        }

        public WebScaffold(string folder)
        {
            this._folder = folder;
            this._uniqueSubPath = Guid.NewGuid().ToString();
            _oldOwnerApi = new OwnerApiTestUtils(SystemProcessApiKey);
        }

        public static HttpClient CreateHttpClient<T>()
        {
            return HttpClientFactory.CreateClient<T>();
        }

        public static HttpClient CreateDefaultHttpClient()
        {
            return HttpClientFactory.CreateClient("no-cookies-no-redirects");
        }

        public void RunBeforeAnyTests(bool initializeIdentity = true, bool setupOwnerAccounts = true, Dictionary<string, string> envOverrides = null)
        {
            _testInstancePrefix = Guid.NewGuid().ToString("N");

            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            Environment.SetEnvironmentVariable("Development__SslSourcePath", "./https/");
            Environment.SetEnvironmentVariable("Development__PreconfiguredDomains",
                $"[{string.Join(",", TestIdentities.All.Values.Select(v => $"\"{v.OdinId}\""))}]");

            Environment.SetEnvironmentVariable("Registry__ProvisioningDomain", "provisioning.dotyou.cloud");
            Environment.SetEnvironmentVariable("Registry__ManagedDomains", "[\"dev.dotyou.cloud\"]");
            Environment.SetEnvironmentVariable("Registry__DnsTargetRecordType", "[\"dev.dotyou.cloud\"]");
            Environment.SetEnvironmentVariable("Registry__DnsTargetAddress", "[\"dev.dotyou.cloud\"]");

            Environment.SetEnvironmentVariable("Registry__DnsRecordValues__ApexARecords", "[\"127.0.0.1\"]");
            Environment.SetEnvironmentVariable("Registry__DnsRecordValues__ApexAliasRecord", "provisioning.dotyou.cloud");
            Environment.SetEnvironmentVariable("Registry__DnsRecordValues__WwwCnameTarget", "");
            Environment.SetEnvironmentVariable("Registry__DnsRecordValues__CApiCnameTarget", "");
            Environment.SetEnvironmentVariable("Registry__DnsRecordValues__FileCnameTarget", "");

            Environment.SetEnvironmentVariable("Host__TenantDataRootPath", Path.Combine(TestDataPath, "tenants"));
            Environment.SetEnvironmentVariable("Host__SystemDataRootPath", Path.Combine(TestDataPath, "system"));
            Environment.SetEnvironmentVariable("Host__IPAddressListenList", "[{ \"Ip\": \"*\",\"HttpsPort\": 443,\"HttpPort\": 80 }]");
            Environment.SetEnvironmentVariable("Host__SystemProcessApiKey", SystemProcessApiKey.ToString());

            Environment.SetEnvironmentVariable("Logging__LogFilePath", LogFilePath);
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

            Environment.SetEnvironmentVariable("Mailgun__ApiKey", "dontcare");
            Environment.SetEnvironmentVariable("Mailgun__DefaultFromEmail", "no-reply@odin.earth");
            Environment.SetEnvironmentVariable("Mailgun__EmailDomain", "odin.earth");
            Environment.SetEnvironmentVariable("Mailgun__Enabled", "false");

            Environment.SetEnvironmentVariable("Admin__ApiEnabled", "true");
            Environment.SetEnvironmentVariable("Admin__ApiKey", "your-secret-api-key-here");
            Environment.SetEnvironmentVariable("Admin__ApiKeyHttpHeaderName", "Odin-Admin-Api-Key");
            Environment.SetEnvironmentVariable("Admin__ApiPort", "4444");
            Environment.SetEnvironmentVariable("Admin__Domain", "admin.dotyou.cloud");

            if (envOverrides != null)
            {
                foreach (var (key, value) in envOverrides)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            CreateData();
            CreateLogs();

            _webserver = Program.CreateHostBuilder(Array.Empty<string>()).Build();
            _webserver.Start();

            if (setupOwnerAccounts)
            {
                foreach (var odinId in TestIdentities.All.Keys)
                {
                    _oldOwnerApi.SetupOwnerAccount((OdinId)odinId, initializeIdentity).GetAwaiter().GetResult();
                }
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
            var client = HttpClientFactory.CreateClient("AnonymousApiHttpClient");
            client.Timeout = TimeSpan.FromMinutes(15);
            client.DefaultRequestHeaders.Add(OdinHeaderNames.FileSystemTypeHeader, Enum.GetName(fileSystemType));
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


        public OdinClientErrorCode GetErrorCode(ApiException apiException)
        {
            var problemDetails = OdinSystemSerializer.Deserialize<ProblemDetails>(apiException.Content!);
            Assert.IsNotNull(problemDetails);
            return (OdinClientErrorCode)int.Parse(problemDetails.Extensions["errorCode"].ToString() ?? string.Empty);
        }

        private void CreateData()
        {
            Directory.CreateDirectory(TestDataPath);
            Directory.CreateDirectory(LogFilePath);
        }

        private void DeleteData()
        {
            if (Directory.Exists(TestDataPath))
            {
                Console.WriteLine($"Removing data in [{TestDataPath}]");
                Directory.Delete(TestDataPath, true);
            }

            if (Directory.Exists(LogFilePath))
            {
                Console.WriteLine($"Removing data in [{LogFilePath}]");
                Directory.Delete(LogFilePath, true);
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
                var p = PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "testsdata", _uniqueSubPath, "data", _folder);
                string x = isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
                return Path.Combine(_testInstancePrefix, x);
            }
        }

        private bool isDev => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        private string home => Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("HOMEPATH");

        private string LogFilePath
        {
            get
            {
                var p = PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "testsdata", _uniqueSubPath, "logs", _folder);
                string x = isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
                return Path.Combine(_testInstancePrefix, x);
            }
        }

        /// <summary>
        /// Total transitionary hack method until i refactor the API clients 
        /// </summary>
        public static UploadManifestPayloadDescriptor CreatePayloadDescriptorFrom(string payloadKey, params ThumbnailDescriptor[] thumbs)
        {
            var thumbList = thumbs?.Select(t => new UploadedManifestThumbnailDescriptor()
            {
                ThumbnailKey = t.GetFilename(WebScaffold.PAYLOAD_KEY),
                PixelWidth = t.PixelWidth,
                PixelHeight = t.PixelHeight
            });
            
            return new()
            {
                PayloadKey = payloadKey,
                Thumbnails = thumbList
            };
        }
    }
}