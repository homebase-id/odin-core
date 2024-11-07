using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HttpClientFactoryLite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Core.Util;
using Odin.Hosting.Tests._Universal.ApiClient.App;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.AppAPI.ApiClient;
using Odin.Hosting.Tests.AppAPI.ApiClient.Base;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Odin.Services.Authorization.ExchangeGrants;
using Refit;
using Serilog.Events;

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

        public const string HttpPort = "8080";
        public const string HttpsPort = "8443";
        public const string AdminPort = "4444";

        private readonly string _folder;


        // private readonly string _password = "EnSøienØ";
        private IHost _webserver;

        private readonly OwnerApiTestUtils _oldOwnerApi;

        // private readonly OwnerApiClient _ownerApiClient;
        private AppApiTestUtils _appApi;
        private ScenarioBootstrapper _scenarios;
        private readonly string _uniqueSubPath;
        private string _testInstancePrefix;
        private Action<Dictionary<LogEventLevel, List<LogEvent>>> _assertLogEvents;

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

        public WebScaffold(string folder, string fixedSubPath = null)
        {
            this._folder = folder;
            this._uniqueSubPath = fixedSubPath ?? Guid.NewGuid().ToString();
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

        public void RunBeforeAnyTests(
            bool initializeIdentity = true,
            bool setupOwnerAccounts = true,
            Dictionary<string, string> envOverrides = null)
        {
            // This will trigger any finalizers that are waiting to be run.
            // This is useful to verify that all db's are correctly disposed.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            _assertLogEvents = null;
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
            Environment.SetEnvironmentVariable("Registry__DnsRecordValues__CApiCnameTarget", "");
            Environment.SetEnvironmentVariable("Registry__DnsRecordValues__FileCnameTarget", "");

            Environment.SetEnvironmentVariable("Host__TenantDataRootPath", Path.Combine(TestDataPath, "tenants"));
            Environment.SetEnvironmentVariable("Host__SystemDataRootPath", Path.Combine(TestDataPath, "system"));
            Environment.SetEnvironmentVariable("Host__IPAddressListenList__0__HttpPort", HttpPort);
            Environment.SetEnvironmentVariable("Host__IPAddressListenList__0__HttpsPort", HttpsPort);
            Environment.SetEnvironmentVariable("Host__IPAddressListenList__0__Ip", "*");
            Environment.SetEnvironmentVariable("Host__SystemProcessApiKey", SystemProcessApiKey.ToString());
            Environment.SetEnvironmentVariable("Host__IpRateLimitRequestsPerSecond", int.MaxValue.ToString());

            Environment.SetEnvironmentVariable("Logging__LogFilePath", LogFilePath);
            Environment.SetEnvironmentVariable("Logging__EnableStatistics", "true");

            Console.WriteLine($"Log file Path: [{LogFilePath}]");

            Environment.SetEnvironmentVariable("Job__Enabled", "false");
            Environment.SetEnvironmentVariable("Job__ConnectionPooling", "false");
            Environment.SetEnvironmentVariable("Job__EnableJobBackgroundService", "false");
            Environment.SetEnvironmentVariable("Job__CronBatchSize", "100");
            Environment.SetEnvironmentVariable("Job__BackgroundJobStartDelaySeconds", "10");
            Environment.SetEnvironmentVariable("Job__CronProcessingInterval", "5");
            Environment.SetEnvironmentVariable("Job__EnsureCertificateProcessorIntervalSeconds", "1000");
            Environment.SetEnvironmentVariable("Job__ProcessPendingCertificateOrderIntervalInSeconds", "1000");

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
            Environment.SetEnvironmentVariable("Admin__ApiPort", AdminPort);
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

        public void RunAfterAnyTests(Action<Dictionary<LogEventLevel, List<LogEvent>>> assertLogEvents = null)
        {
            var logEvents = Services.GetRequiredService<ILogEventMemoryStore>().GetLogEvents();

            if (null != _webserver)
            {
                _webserver.StopAsync().GetAwaiter().GetResult();
                _webserver.Dispose();
            }

            this.DeleteData();
            this.DeleteLogs();

            // This will trigger any finalizers that are waiting to be run.
            // This is useful to verify that all db's are correctly disposed.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Make sure this is last so it doesnt mess up the rest of the cleanup
            AssertLogEvents(logEvents, assertLogEvents);
        }

        public OwnerApiTestUtils OldOwnerApi =>
            this._oldOwnerApi ?? throw new NullReferenceException("Check if the owner app was initialized in method RunBeforeAnyTests");

        public OwnerApiClient CreateOwnerApiClient(TestIdentity identity)
        {
            return new OwnerApiClient(this._oldOwnerApi, identity);
        }

        public OwnerApiClientRedux CreateOwnerApiClientRedux(TestIdentity identity)
        {
            return new OwnerApiClientRedux(this._oldOwnerApi, identity);
        }

        public AppApiClientRedux CreateAppApiClientRedux(OdinId identity, ClientAccessToken accessToken)
        {
            return new AppApiClientRedux(identity, accessToken.ToAuthenticationToken(), accessToken.SharedSecret.GetKey());
        }

        public AppApiClient CreateAppClient(TestIdentity identity, Guid appId)
        {
            return new AppApiClient(this._oldOwnerApi, identity, appId);
        }

        public AppApiTestUtils AppApi => this._appApi ??
                                         throw new NullReferenceException(
                                             "Check if the owner app was initialized in method RunBeforeAnyTests");

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
            client.BaseAddress = new Uri($"https://{identity}:{HttpsPort}");
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
        public static UploadManifestPayloadDescriptor CreatePayloadDescriptorFrom(string payloadKey, bool excludeIv = false,
            params ThumbnailDescriptor[] thumbs)
        {
            var thumbList = thumbs?.Select(t => new UploadedManifestThumbnailDescriptor()
            {
                ThumbnailKey = t.GetFilename(WebScaffold.PAYLOAD_KEY),
                PixelWidth = t.PixelWidth,
                PixelHeight = t.PixelHeight
            });

            return new()
            {
                Iv = excludeIv ? null : ByteArrayUtil.GetRndByteArray(16),
                PayloadKey = payloadKey,
                Thumbnails = thumbList
            };
        }

        //

        public void AssertLogEvents(Action<Dictionary<LogEventLevel, List<LogEvent>>> assertLogEvents = null)
        {
            var logEvents = Services.GetRequiredService<ILogEventMemoryStore>().GetLogEvents();
            AssertLogEvents(logEvents, assertLogEvents);
        }

        public Dictionary<LogEventLevel, List<LogEvent>> GetLogEvents()
        {
            var logEvents = Services.GetRequiredService<ILogEventMemoryStore>().GetLogEvents();
            return logEvents;
        }

        public void ClearLogEvents()
        {
            Services.GetRequiredService<ILogEventMemoryStore>().Clear();
        }

        public void ClearAssertLogEventsAction()
        {
            _assertLogEvents = null;
        }

        public void SetAssertLogEventsAction(Action<Dictionary<LogEventLevel, List<LogEvent>>> logEventsAction)
        {
            _assertLogEvents = logEventsAction;
        }

        private void AssertLogEvents(
            Dictionary<LogEventLevel, List<LogEvent>> logEvents,
            Action<Dictionary<LogEventLevel, List<LogEvent>>> assertLogEvents)
        {
            _assertLogEvents ??= assertLogEvents ?? DefaultAssertLogEvents;
            _assertLogEvents(logEvents);
        }

        private static void DefaultAssertLogEvents(Dictionary<LogEventLevel, List<LogEvent>> logEvents)
        {
            Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(0), "Unexpected number of Error log events");
            Assert.That(logEvents[LogEventLevel.Fatal].Count, Is.EqualTo(0), "Unexpected number of Fatal log events");
        }

        public void AssertHasDebugLogEvent(string message, int count)
        {
            var logEvents = GetLogEvents();
            var expectedEvent = logEvents[LogEventLevel.Debug].Where(l => l.RenderMessage() == message);
            Assert.IsTrue(expectedEvent.Count() == count);
        }
    }
}