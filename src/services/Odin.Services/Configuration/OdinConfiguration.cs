using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Configuration;
using Odin.Core.Configuration;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
using Odin.Services.Certificate;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Email;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Configuration
{
    public class OdinConfiguration
    {
        public HostSection Host { get; init; }

        public RegistrySection Registry { get; init; }

        public DevelopmentSection Development { get; init; }

        public LoggingSection Logging { get; init; }
        public BackgroundServicesSection BackgroundServices { get; init; }
        public CertificateRenewalSection CertificateRenewal { get; init; }

        public MailgunSection Mailgun { get; init; }
        public AdminSection Admin { get; init; }

        public FeedSection Feed { get; init; }
        public TransitSection Transit { get; init; }

        public PushNotificationSection PushNotification { get; init; }
        public DatabaseSection Database { get; init; }

        public RedisSection Redis { get; init; }
        public CacheSection Cache { get; init; }

        public S3PayloadStorageSection S3PayloadStorage { get; init; } = new();

        public OdinConfiguration()
        {
            // Mockable support
        }

        public OdinConfiguration(IConfiguration config)
        {
            Host = new HostSection(config);
            Logging = new LoggingSection(config);
            BackgroundServices = new BackgroundServicesSection(config);
            Registry = new RegistrySection(config);
            Mailgun = new MailgunSection(config);
            Admin = new AdminSection(config);

            Feed = new FeedSection(config);
            Transit = new TransitSection(config);

            if (config.SectionExists("Development"))
            {
                Development = new DevelopmentSection(config);
            }

            CertificateRenewal = new CertificateRenewalSection(config);
            PushNotification = new PushNotificationSection(config);
            Database = new DatabaseSection(config);
            Redis = new RedisSection(config);
            Cache = new CacheSection(config);
            S3PayloadStorage = new S3PayloadStorageSection(config);
        }

        //

        public class TransitSection
        {
            public TransitSection()
            {
                // Mockable support
            }

            public TransitSection(IConfiguration config)
            {
            }
        }

        public class FeedSection
        {
            public FeedSection()
            {
                // Mockable support
            }

            public FeedSection(IConfiguration config)
            {
                MaxCommentsInPreview = config.GetOrDefault("Feed:MaxCommentsInPreview", 3);
            }
            
            public int MaxCommentsInPreview { get; init; }
        }

        /// <summary>
        /// Settings specific to the development/demo process
        /// </summary>
        public class DevelopmentSection
        {
            public DevelopmentSection()
            {
                // Mockable support
            }

            public DevelopmentSection(IConfiguration config)
            {
                PreconfiguredDomains = config.GetOrDefault("Development:PreconfiguredDomains", new List<string>());
                SslSourcePath = config.Required<string>("Development:SslSourcePath");
                RecoveryKeyWaitingPeriodSeconds = config.Required<int>("Development:RecoveryKeyWaitingPeriodSeconds");
            }

            public List<string> PreconfiguredDomains { get; init; }
            public string SslSourcePath { get; init; }
            public double RecoveryKeyWaitingPeriodSeconds { get; init; }
        }

        public class RegistrySection
        {
            public List<string> InvitationCodes { get; init; }

            public string PowerDnsHostAddress { get; init; }
            public string PowerDnsApiKey { get; init; }

            public string ProvisioningDomain { get; init; }
            public bool ProvisioningEnabled { get; init; }
            
            public List<ManagedDomainApex> ManagedDomainApexes { get; init; }

            public DnsConfigurationSet DnsConfigurationSet { get; init; }
            public List<string> DnsResolvers { get; init; }
            public long DaysUntilAccountDeletion { get; init; }

            public RegistrySection()
            {
                // Mockable support
            }

            public RegistrySection(IConfiguration config)
            {
                PowerDnsHostAddress = config.GetOrDefault("Registry:PowerDnsHostAddress", "localhost");
                PowerDnsApiKey = config.GetOrDefault("Registry:PowerDnsApiKey", "");
                ProvisioningDomain = config.Required<string>("Registry:ProvisioningDomain").Trim().ToLower();
                ProvisioningEnabled = config.GetOrDefault("Registry:ProvisioningEnabled", false);
                AsciiDomainNameValidator.AssertValidDomain(ProvisioningDomain);
                ManagedDomainApexes = config.GetOrDefault("Registry:ManagedDomainApexes", new List<ManagedDomainApex>());
                DnsResolvers = config.GetOrDefault("Registry:DnsResolvers", new List<string> { "1.1.1.1", "8.8.8.8", "9.9.9.9", "208.67.222.222" });
                DnsConfigurationSet = new DnsConfigurationSet(
                    config.Required<List<string>>("Registry:DnsRecordValues:ApexARecords").First(), // SEB:NOTE we currently only allow one A record
                    config.Required<string>("Registry:DnsRecordValues:ApexAliasRecord"));

                InvitationCodes = config.GetOrDefault("Registry:InvitationCodes", new List<string>());

                DaysUntilAccountDeletion = config.GetOrDefault("Registry:DaysUntilAccountDeletion", 30);
            }

            public class ManagedDomainApex
            {
                public string Apex { get; init; } = "";
                public List<string> PrefixLabels { get; init; } = new();
            }
        }

        public class HostSection
        {
            public string TenantDataRootPath { get; init; }
            public string SystemDataRootPath { get; init; }
            public string SystemSslRootPath { get; init; }
            public string DataProtectionKeyPath { get; init; }
            public bool Http1Only { get; init; }

            /// <summary>
            /// List of IPv4 or IPv6 IP address on which to listen 
            /// </summary>
            public List<ListenEntry> IPAddressListenList { get; init; }

            public int CacheSlidingExpirationSeconds { get; init; }

            public int ShutdownTimeoutSeconds { get; init; }
            public Guid SystemProcessApiKey { get; set; }
            
            public int IpRateLimitRequestsPerSecond { get; init; }

            public HostSection()
            {
                // Mockable support
            }

            public HostSection(IConfiguration config)
            {
                TenantDataRootPath =
                    Env.ExpandEnvironmentVariablesCrossPlatform(config.Required<string>("Host:TenantDataRootPath"));
                
                SystemDataRootPath =                 
                    Env.ExpandEnvironmentVariablesCrossPlatform(config.Required<string>("Host:SystemDataRootPath"));

                SystemSslRootPath = Path.Combine(SystemDataRootPath, TenantPathManager.SslFolder);

                DataProtectionKeyPath = Path.Combine(SystemDataRootPath, "tmp", "data-protection-keys");

                Http1Only = config.GetOrDefault("Host:Http1Only", false);

                IPAddressListenList = config.Required<List<ListenEntry>>("Host:IPAddressListenList");

                CacheSlidingExpirationSeconds = config.Required<int>("Host:CacheSlidingExpirationSeconds");

                HomePageCachingExpirationSeconds = config.GetOrDefault("Host:HomePageCachingExpirationSeconds", 5 * 60);

                ShutdownTimeoutSeconds = config.GetOrDefault("Host:ShutdownTimeoutSeconds", 120);
                SystemProcessApiKey = config.GetOrDefault("Host:SystemProcessApiKey", Guid.NewGuid());

                //TODO: changed to required when Seb and I can coordinate config changes
                PushNotificationSubject = config.GetOrDefault("Host:PushNotificationSubject", "mailto:info@homebase.id");
                PushNotificationBatchSize = config.GetOrDefault("Host:PushNotificationBatchSize", 100);

                FileOperationRetryAttempts = config.GetOrDefault("Host:FileOperationRetryAttempts", 8);
                FileOperationRetryDelayMs = TimeSpan.FromMilliseconds(config.GetOrDefault("Host:FileOperationRetryDelayMs", 100));

                FileWriteChunkSizeInBytes = config.GetOrDefault("Host:FileWriteChunkSizeInBytes", 1024);

                PeerOperationMaxAttempts = config.GetOrDefault("Host:PeerOperationMaxAttempts", 3);
                PeerOperationDelayMs = TimeSpan.FromMilliseconds(config.GetOrDefault("Host:PeerOperationDelayMs", 300));

                OutboxOperationMaxAttempts = config.GetOrDefault("Host:OutboxOperationMaxAttempts", 30);

                ReportContentUrl = config.GetOrDefault<string>("Host:ReportContentUrl");

                InboxOutboxRecoveryAgeSeconds = config.GetOrDefault("Host:InboxOutboxRecoveryAgeSeconds", 24 * 60 * 60);

                // SEB:TODO figure out what the rate limit should default to. FE requests an insane amount of files in development mode.
                IpRateLimitRequestsPerSecond = config.GetOrDefault("Host:IpRateLimitRequestsPerSecond", 1000);
            }

            public string ReportContentUrl { get; set; }

            public int DefaultHttpPort => IPAddressListenList.FirstOrDefault()?.HttpPort ?? 80;
            public int DefaultHttpsPort => IPAddressListenList.FirstOrDefault()?.HttpsPort ?? 443;
            public int HomePageCachingExpirationSeconds { get; set; }
            public string PushNotificationSubject { get; set; }

            /// <summary>
            /// Number of times to retry a file.move operation
            /// </summary>
            public int FileOperationRetryAttempts { get; init; }

            /// <summary>
            /// Number of milliseconds to delay between file.move attempts
            /// </summary>
            public TimeSpan FileOperationRetryDelayMs { get; init; }

            /// <summary>
            /// Specifies the number of bytes to write when writing a stream to disk in chunks
            /// </summary>
            public int FileWriteChunkSizeInBytes { get; set; }

            public int PushNotificationBatchSize { get; set; }
            public int PeerOperationMaxAttempts { get; init; }
            public int OutboxOperationMaxAttempts { get; init; }
            
            public TimeSpan PeerOperationDelayMs { get; init; }

            /// <summary>
            /// The age in seconds of items that should be recovered which have been
            /// popped (checked out) of the inbox/outbox queue w/o having been marked complete or failed
            /// </summary>
            public int InboxOutboxRecoveryAgeSeconds { get; init; }
        }

        public class ListenEntry
        {
            public string Ip { get; init; } = "";
            public int HttpsPort { get; init; } = 0;
            public int HttpPort { get; init; } = 0;

            public IPAddress GetIp()
            {
                return this.Ip == "*" ? IPAddress.Any : IPAddress.Parse(this.Ip);
            }
        }

        //

        public class BackgroundServicesSection
        {
            public int EnsureCertificateProcessorIntervalSeconds { get; init; }
            public int InboxOutboxReconciliationIntervalSeconds { get; init; }
            public int JobCleanUpIntervalSeconds { get; init; }
            public bool SystemBackgroundServicesEnabled { get; init; }
            public bool TenantBackgroundServicesEnabled { get; init; }

            public BackgroundServicesSection()
            {
                // Mockable support
            }

            public BackgroundServicesSection(IConfiguration config)
            {
                EnsureCertificateProcessorIntervalSeconds = config.Required<int>("BackgroundServices:EnsureCertificateProcessorIntervalSeconds");
                InboxOutboxReconciliationIntervalSeconds = config.Required<int>("BackgroundServices:InboxOutboxReconciliationIntervalSeconds");
                JobCleanUpIntervalSeconds = config.Required<int>("BackgroundServices:JobCleanUpIntervalSeconds");
                SystemBackgroundServicesEnabled = config.GetOrDefault("BackgroundServices:SystemBackgroundServicesEnabled", true);
                TenantBackgroundServicesEnabled = config.GetOrDefault("BackgroundServices:TenantBackgroundServicesEnabled", true);
            }
        }

        //

        public class LoggingSection
        {
            public string LogFilePath { get; init; }
            public bool EnableStatistics { get; init; }

            public LoggingSection()
            {
                // Mockable support
            }

            public LoggingSection(IConfiguration config)
            {
                LogFilePath = Env.ExpandEnvironmentVariablesCrossPlatform(config.GetOrDefault("Logging:LogFilePath", ""));
                EnableStatistics = config.GetOrDefault("Logging:EnableStatistics", false);
            }
        }

        //

        public class CertificateRenewalSection
        {
            public bool UseCertificateAuthorityProductionServers { get; init; }
            public string CertificateAuthorityAssociatedEmail { get; init; }
            public byte[] StorageKey { get; init; }

            public CertificateRenewalSection()
            {
                // Mockable support
            }

            public CertificateRenewalSection(IConfiguration config)
            {
                UseCertificateAuthorityProductionServers = config.Required<bool>("CertificateRenewal:UseCertificateAuthorityProductionServers");
                CertificateAuthorityAssociatedEmail = config.Required<string>("CertificateRenewal:CertificateAuthorityAssociatedEmail");
                StorageKey = Convert.FromHexString(config.Required<string>("CertificateRenewal:StorageKey"));
                if (StorageKey.Length != 32)
                {
                    throw new OdinConfigException("CertificateRenewal:StorageKey must be a 32-byte hex string");
                }
            }
        }

        //

        public class MailgunSection
        {
            public string ApiKey { get; init; }
            public NameAndEmailAddress DefaultFrom { get; init; }
            public string EmailDomain { get; init; }
            public bool Enabled { get; init; }

            public MailgunSection()
            {
                // Mockable support
            }

            public MailgunSection(IConfiguration config)
            {
                Enabled = config.GetOrDefault("Mailgun:Enabled", false);
                if (Enabled)
                {
                    ApiKey = config.Required<string>("Mailgun:ApiKey");
                    DefaultFrom = new NameAndEmailAddress
                    {
                        Email = config.Required<string>("Mailgun:DefaultFromEmail"),
                        Name = config.GetOrDefault("Mailgun:DefaultFromName", ""),
                    };
                    EmailDomain = config.Required<string>("Mailgun:EmailDomain");
                }
            }
        }

        //

        public class AdminSection
        {
            public bool ApiEnabled { get; init; }
            public string ApiKey { get; init; }
            public string ApiKeyHttpHeaderName { get; init; }
            public int ApiPort { get; init; }
            public string Domain { get; init; }
            public string ExportTargetPath { get; init; }

            public AdminSection()
            {
                // Mockable support
            }

            public AdminSection(IConfiguration config)
            {
                ApiEnabled = config.GetOrDefault("Admin:ApiEnabled", false);
                if (ApiEnabled)
                {
                    ApiKey = config.Required<string>("Admin:ApiKey");
                    ApiKeyHttpHeaderName = config.Required<string>("Admin:ApiKeyHttpHeaderName");
                    ApiPort = config.Required<int>("Admin:ApiPort");
                    Domain = config.Required<string>("Admin:Domain");
                    ExportTargetPath = config.Required<string>("Admin:ExportTargetPath");
                }
            }
        }

        //

        public class PushNotificationSection
        {
            public string BaseUrl { get; init; }

            public PushNotificationSection()
            {
                // Mockable support
            }

            public PushNotificationSection(IConfiguration config)
            {
                BaseUrl = config.GetOrDefault("PushNotification:BaseUrl", "https://push.homebase.id");
            }
        }
        
        //

        public class DatabaseSection
        {
            public DatabaseType Type { get; init; }
            public string ConnectionString { get; init; } = "";

            public DatabaseSection()
            {
                // Mockable support
            }

            public DatabaseSection(IConfiguration config)
            {
                Type = config.GetOrDefault("Database:Type", DatabaseType.Sqlite);
                if (Type != DatabaseType.Sqlite) // Sqlite doesn't require a connection string
                {
                    ConnectionString = config.Required<string>("Database:ConnectionString");        
                }
            }
        }

        //

        public class RedisSection
        {
            public bool Enabled { get; init; }
            public string Configuration { get; init; } = "";

            public RedisSection()
            {
                // Mockable support
            }

            public RedisSection(IConfiguration config)
            {
                Enabled = config.GetOrDefault("Redis:Enabled", false);
                if (Enabled)
                {
                    Configuration = config.Required<string>("Redis:Configuration");
                }
            }
        }

        //

        public class CacheSection
        {
            public Level2CacheType Level2CacheType { get; init; }

            public CacheSection()
            {
                // Mockable support
            }

            public CacheSection(IConfiguration config)
            {
                Level2CacheType = config.GetOrDefault("Cache:Level2CacheType", Level2CacheType.None);
            }
        }

        //

        public class S3PayloadStorageSection
        {
            public bool Enabled { get; init; }
            public string AccessKey { get; init; } = "";
            public string SecretAccessKey { get; init; } = "";
            public string ServiceUrl { get; init; } = "";
            public string Region { get; init; } = "";
            public bool ForcePathStyle { get; init; } = false;
            public string BucketName { get; init; } = "";

            public S3PayloadStorageSection()
            {
                // Mockable support
            }

            public S3PayloadStorageSection(IConfiguration config)
            {
                Enabled = config.GetOrDefault("S3PayloadStorage:Enabled", false);
                if (Enabled)
                {
                    AccessKey = config.Required<string>("S3PayloadStorage:AccessKey");
                    SecretAccessKey = config.Required<string>("S3PayloadStorage:SecretAccessKey");
                    ServiceUrl = config.Required<string>("S3PayloadStorage:ServiceUrl");
                    Region = config.GetOrDefault("S3PayloadStorage:Region", "");
                    ForcePathStyle = config.GetOrDefault("S3PayloadStorage:ForcePathStyle", false);
                    BucketName = config.Required<string>("S3PayloadStorage:BucketName");
                }
            }
        }
    }
}