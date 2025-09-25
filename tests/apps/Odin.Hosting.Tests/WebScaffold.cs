using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Exceptions;
using Odin.Core.Http;
using Odin.Core.Identity;
using Odin.Core.Logging.Statistics.Serilog;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Util;
using Odin.Hosting.Tests._Universal.ApiClient.App;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.AppAPI.ApiClient;
using Odin.Hosting.Tests.AppAPI.Utils;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Hosting.Tests.OwnerApi.Utils;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Test.Helpers.Logging;
using Refit;
using Serilog.Events;
using System;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

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
        public static readonly IDynamicHttpClientFactory HttpClientFactory =
            new DynamicHttpClientFactory(NullLogger<DynamicHttpClientFactory>.Instance);

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
        public ILogger<WebScaffold> Logger { get; }

#if RUN_POSTGRES_TESTS
        protected PostgreSqlContainer PostgresContainer;
#endif

#if RUN_REDIS_TESTS
        protected RedisContainer  RedisContainer;
#endif

#if RUN_S3_TESTS
        protected MinioContainer MinioContainer = null!;
#endif

        public WebScaffold(string folder, string fixedSubPath = null)
        {
            this._folder = folder;
            this._uniqueSubPath = fixedSubPath ?? Guid.NewGuid().ToString();
            _oldOwnerApi = new OwnerApiTestUtils(SystemProcessApiKey);

            // NOTE: we create a separate logger for the scaffold to avoid mixing with the host logger.
            // Logging errors in the scaffold logger will NOT fail tests.
            // You can get the host logger like this: Services.GetRequiredService<ILogger<WebScaffold>>();
            Logger = TestLogFactory.CreateConsoleLogger<WebScaffold>(LogEventLevel.Verbose);
        }

        public void RunBeforeAnyTests(
            bool initializeIdentity = true,
            bool setupOwnerAccounts = true,
            Dictionary<string, string> envOverrides = null,
            List<TestIdentity> testIdentities = null
        )
        {
            // Default to all identities
            TestIdentities.SetCurrent(testIdentities);
            CryptographyConstants.ITERATIONS = 3; // Override for tests

            // This will trigger any finalizers that are waiting to be run.
            // This is useful to verify that all db's are correctly disposed.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            _assertLogEvents = null;
            _testInstancePrefix = Guid.NewGuid().ToString("N");

            Environment.SetEnvironmentVariable("Database__Type", "sqlite");
#if RUN_POSTGRES_TESTS
            PostgresContainer = new PostgreSqlBuilder()
                .WithImage("postgres:latest")
                .WithDatabase("odin")
                .WithUsername("odin")
                .WithPassword("odin")
                .Build();
            PostgresContainer.StartAsync().GetAwaiter().GetResult();
            Environment.SetEnvironmentVariable("Database__Type", "postgres");
            Environment.SetEnvironmentVariable("Database__ConnectionString", PostgresContainer.GetConnectionString());
            // Environment.SetEnvironmentVariable("Serilog__MinimumLevel__Override__Odin.Core.Storage.Database.System.Connection.ScopedSystemConnectionFactory", "Verbose");
            // Environment.SetEnvironmentVariable("Serilog__MinimumLevel__Override__Odin.Core.Storage.Database.Identity.Connection.ScopedIdentityConnectionFactory", "Verbose");
#endif

            Environment.SetEnvironmentVariable("Redis__Enabled", "false");
#if RUN_REDIS_TESTS
            RedisContainer = new RedisBuilder()
                .WithImage("redis:latest")
                .Build();
            RedisContainer.StartAsync().GetAwaiter().GetResult();
            Environment.SetEnvironmentVariable("Redis__Enabled", "true");
            Environment.SetEnvironmentVariable("Redis__Configuration", RedisContainer.GetConnectionString());
            Environment.SetEnvironmentVariable("Cache__Level2CacheType", "redis");
#endif

            Environment.SetEnvironmentVariable("S3PayloadStorage__Enabled", "false");
#if RUN_S3_TESTS
            Logger.LogInformation("Starting Minio S3 container for tests");
            MinioContainer = new MinioBuilder()
                .WithImage("minio/minio:RELEASE.2025-05-24T17-08-30Z")
                .WithUsername("minioadmin")
                .WithPassword("minioadmin123")
                .Build();
            MinioContainer.StartAsync().GetAwaiter().GetResult();
            Environment.SetEnvironmentVariable("S3PayloadStorage__Enabled", "true");
            Environment.SetEnvironmentVariable("S3PayloadStorage__AccessKey", MinioContainer.GetAccessKey());
            Environment.SetEnvironmentVariable("S3PayloadStorage__SecretAccessKey", MinioContainer.GetSecretKey());
            Environment.SetEnvironmentVariable("S3PayloadStorage__ServiceUrl", MinioContainer.GetConnectionString());
            Environment.SetEnvironmentVariable("S3PayloadStorage__Region", "meh");
            Environment.SetEnvironmentVariable("S3PayloadStorage__ForcePathStyle", "true");
            Environment.SetEnvironmentVariable("S3PayloadStorage__BucketName", "odin-payloads");
#endif

            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            Environment.SetEnvironmentVariable("Development__SslSourcePath", "./https/");
            Environment.SetEnvironmentVariable("Development__PreconfiguredDomains",
                $"[{string.Join(",", TestIdentities.InitializedIdentities.Values.Select(v => $"\"{v.OdinId}\""))}]");

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

            Console.WriteLine($"Data path: [{TestDataPath}]");
            Console.WriteLine($"Log file Path: [{LogFilePath}]");

            Environment.SetEnvironmentVariable("BackgroundServices__EnsureCertificateProcessorIntervalSeconds", "1000");

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

            _webserver = Program.CreateHostBuilder([]).Build().BeforeApplicationStarting([]);
            _webserver.Start();

            if (setupOwnerAccounts)
            {
                // foreach (var odinId in TestIdentities.All.Keys)
                // {
                //     _oldOwnerApi.SetupOwnerAccount((OdinId)odinId, initializeIdentity).GetAwaiter().GetResult();
                // }

                Parallel.ForEach(TestIdentities.InitializedIdentities.Values.Select(i => i.OdinId),
                    odinId => { _oldOwnerApi.SetupOwnerAccount(odinId, initializeIdentity).GetAwaiter().GetResult(); });

                //Parallel.ForEach(TestIdentities.All.Keys,
                //    odinId => { _oldOwnerApi.SetupOwnerAccount((OdinId)odinId, initializeIdentity).GetAwaiter().GetResult(); });
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

#if RUN_POSTGRES_TESTS
            PostgresContainer?.DisposeAsync().AsTask().Wait();
            PostgresContainer = null;
#endif

#if RUN_REDIS_TESTS
            RedisContainer?.StopAsync().Wait();
            RedisContainer?.DisposeAsync().AsTask().Wait();
            RedisContainer = null;
#endif

#if RUN_S3_TESTS
            MinioContainer?.StopAsync().Wait();
            MinioContainer?.DisposeAsync().AsTask().Wait();
            MinioContainer = null;
#endif

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
            ClassicAssert.IsNotNull(problemDetails);
            return Enum.Parse<OdinClientErrorCode>(problemDetails.Extensions["errorCode"].ToString()!, true);
        }

        private void CreateData()
        {
            Directory.CreateDirectory(TestDataPath);
            Directory.CreateDirectory(LogFilePath);
        }

        public void DeleteData()
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

        public string TestPayloadPath
        {
            get
            {
                var p = PathUtil.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "payloads", _uniqueSubPath, "dotyoudata", _folder);
                string x = isDev ? PathUtil.Combine(home, p.Substring(1)) : p;
                return Path.Combine(_testInstancePrefix, x);
            }
        }

        public string TestDataPath
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

#if CI_GITHUB
        public TimeSpan DebugTimeout { get; } = TimeSpan.FromMinutes(3);
#else
        public TimeSpan DebugTimeout { get; } = TimeSpan.FromMinutes(60);
#endif
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

        public void DumpLogEventsToConsole()
        {
            Console.WriteLine("--------======== Log Events Begin ========--------");

            var logEvents = new List<LogEvent>();
            var keyedLogEvents = GetLogEvents();
            foreach (var (level, events) in keyedLogEvents)
            {
                logEvents.AddRange(events);
            }

            logEvents.Sort((a, b) => a.Timestamp < b.Timestamp ? -1 : 1);
            foreach (var logEvent in logEvents)
            {
                Console.WriteLine($"{logEvent.Timestamp.ToUnixTimeMilliseconds()} {logEvent.RenderMessage()}");
            }

            Console.WriteLine("--------======== Log Events End ========--------");
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
            LogEvents.DumpEvents(logEvents[LogEventLevel.Error]);
            Assert.That(logEvents[LogEventLevel.Error].Count, Is.EqualTo(0), "Unexpected number of Error log events");

            LogEvents.DumpEvents(logEvents[LogEventLevel.Fatal]);
            Assert.That(logEvents[LogEventLevel.Fatal].Count, Is.EqualTo(0), "Unexpected number of Fatal log events");
        }

        public void AssertHasDebugLogEvent(string message, int count)
        {
            var logEvents = GetLogEvents();
            var expectedEvent = logEvents[LogEventLevel.Debug].Where(l => l.RenderMessage() == message);
            ClassicAssert.IsTrue(expectedEvent.Count() == count);
        }

        public async Task<string> WaitForLogPropertyValue(string propertyName, LogEventLevel logLevel, TimeSpan? maxWaitTime = null)
        {
            var maxWait = maxWaitTime ?? TimeSpan.FromSeconds(40);

            var logEvents = Services.GetRequiredService<ILogEventMemoryStore>().GetLogEvents();

            var sw = Stopwatch.StartNew();

            while (true)
            {
                var infoEvents = logEvents[logLevel];
                infoEvents.Reverse(); // note: we reverse since we are always looking for the most recent property
                foreach (var infoEvent in infoEvents)
                {
                    if (infoEvent.Properties.TryGetValue(propertyName, out var value))
                    {
                        return value?.ToString();
                    }
                }

                if (sw.Elapsed > maxWait)
                {
                    throw new TimeoutException($"Failed waiting to find log property {propertyName} in logLevel {logLevel}");
                }

                await Task.Delay(100);
            }
        }
    }
}