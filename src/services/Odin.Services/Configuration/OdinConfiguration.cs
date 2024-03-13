﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Configuration;
using Odin.Core.Configuration;
using Odin.Core.Exceptions;
using Odin.Core.Util;
using Odin.Services.Certificate;
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
        public JobSection Job { get; init; }
        public CertificateRenewalSection CertificateRenewal { get; init; }

        public MailgunSection Mailgun { get; init; }
        public AdminSection Admin { get; init; }

        public FeedSection Feed { get; init; }
        public TransitSection Transit { get; init; }

        public OdinConfiguration()
        {
            // Mockable support
        }

        public OdinConfiguration(IConfiguration config)
        {
            Host = new HostSection(config);
            Logging = new LoggingSection(config);
            Job = new JobSection(config);
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
                OutboxBatchSize = config.Required<int>($"Transit:{nameof(OutboxBatchSize)}");

                if (OutboxBatchSize <= 0)
                {
                    throw new OdinSystemException($"{nameof(OutboxBatchSize)} must be greater than 0");
                }
            }

            public int OutboxBatchSize { get; init; }
        }

        public class FeedSection
        {
            public FeedSection()
            {
                // Mockable support
            }

            public FeedSection(IConfiguration config)
            {
                DistributionBatchSize = config.Required<int>("Feed:DistributionBatchSize");

                if (DistributionBatchSize <= 0)
                {
                    throw new OdinSystemException($"{nameof(DistributionBatchSize)} must be greater than 0");
                }

                MaxCommentsInPreview = config.GetOrDefault("Feed:MaxCommentsInPreview", 3);
            }

            public int DistributionBatchSize { get; init; }

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
                PreconfiguredDomains = config.Required<List<string>>("Development:PreconfiguredDomains");
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
                PowerDnsHostAddress = config.Required<string>("Registry:PowerDnsHostAddress");
                PowerDnsApiKey = config.Required<string>("Registry:PowerDnsApiKey");
                ProvisioningDomain = config.Required<string>("Registry:ProvisioningDomain").Trim().ToLower();
                AsciiDomainNameValidator.AssertValidDomain(ProvisioningDomain);
                ManagedDomainApexes = config.Required<List<ManagedDomainApex>>("Registry:ManagedDomainApexes");
                DnsResolvers = config.Required<List<string>>("Registry:DnsResolvers");
                DnsConfigurationSet = new DnsConfigurationSet(
                    config.Required<List<string>>("Registry:DnsRecordValues:ApexARecords").First(), // SEB:NOTE we currently only allow one A record
                    config.Required<string>("Registry:DnsRecordValues:ApexAliasRecord"),
                    config.GetOrDefault<string>("Registry:DnsRecordValues:WwwCnameTarget", ""),
                    config.GetOrDefault<string>("Registry:DnsRecordValues:CApiCnameTarget", ""),
                    config.GetOrDefault<string>("Registry:DnsRecordValues:FileCnameTarget", ""));

                InvitationCodes = config.GetOrDefault<List<string>>("Registry:InvitationCodes", new List<string>());

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

            public bool Http1Only { get; init; }

            /// <summary>
            /// List of IPv4 or IPv6 IP address on which to listen 
            /// </summary>
            public List<ListenEntry> IPAddressListenList { get; init; }

            public int CacheSlidingExpirationSeconds { get; init; }

            public int ShutdownTimeoutSeconds { get; init; }
            public Guid SystemProcessApiKey { get; set; }

            public HostSection()
            {
                // Mockable support
            }

            public HostSection(IConfiguration config)
            {
                var isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
                var home = Environment.GetEnvironmentVariable("HOME") ?? "";

                var p = config.Required<string>("Host:TenantDataRootPath");
                TenantDataRootPath = isDev && !p.StartsWith(home) ? PathUtil.Combine(home, p.Substring(1)) : p;

                var sd = config.Required<string>("Host:SystemDataRootPath");
                SystemDataRootPath = isDev && !sd.StartsWith(home) ? PathUtil.Combine(home, sd.Substring(1)) : sd;

                SystemSslRootPath = Path.Combine(SystemDataRootPath, "ssl");

                Http1Only = config.GetOrDefault("Host:Http1Only", false);

                IPAddressListenList = config.Required<List<ListenEntry>>("Host:IPAddressListenList");

                CacheSlidingExpirationSeconds = config.Required<int>("Host:CacheSlidingExpirationSeconds");

                HomePageCachingExpirationSeconds = config.GetOrDefault<int>("Host:HomePageCachingExpirationSeconds", 5 * 60);

                ShutdownTimeoutSeconds = config.GetOrDefault("Host:ShutdownTimeoutSeconds", 5);
                SystemProcessApiKey = config.GetOrDefault("Host:SystemProcessApiKey", Guid.NewGuid());

                //TODO: changed to required when Seb and I can coordinate config changes
                PushNotificationSubject = config.GetOrDefault("Host:PushNotificationSubject", "mailto:info@homebase.id");
                PushNotificationBatchSize = config.GetOrDefault("Host:PushNotificationBatchSize", 100);

                FileOperationRetryAttempts = config.GetOrDefault("Host:FileWriteRetryAttempts", 8);
                FileOperationRetryDelayMs = TimeSpan.FromMilliseconds(config.GetOrDefault("Host:FileOperationRetryDelayMs", 100));

                FileWriteChunkSizeInBytes = config.GetOrDefault("Host:FileWriteChunkSizeInBytes", 1024);

                UseConcurrentFileManager = config.GetOrDefault("Host:UseConcurrentFileManager", false);
                PeerOperationMaxAttempts = config.GetOrDefault("Host:PeerOperationMaxAttempts", 3);
                PeerOperationDelayMs = config.GetOrDefault("Host:PeerOperationDelayMs", 300);
                ReportContentUrl = config.GetOrDefault<string>("Host:ReportContentUrl");
            }

            public string ReportContentUrl { get; set; }

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

            public bool UseConcurrentFileManager { get; set; }
            public int PushNotificationBatchSize { get; set; }
            public int PeerOperationMaxAttempts { get; init; }
            public int PeerOperationDelayMs { get; init; }
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

        public class JobSection
        {
            /// <summary>
            /// Toggle if job processing is enabled
            /// </summary>
            public bool Enabled { get; init; }

            /// <summary>
            /// Number of seconds to delay starting background jobs when starting the dotyoucore process
            /// </summary>
            public int BackgroundJobStartDelaySeconds { get; init; }

            public int CronProcessingInterval { get; init; }

            public int EnsureCertificateProcessorIntervalSeconds { get; init; }

            /// <summary>
            /// The interval in which we check for the validation of certificate order
            /// </summary>
            public int ProcessPendingCertificateOrderIntervalInSeconds { get; init; }

            /// <summary>
            ///  The number of items to query from the cron queue each time the job runs 
            /// </summary>
            public int CronBatchSize { get; init; }

            public bool EnableJobBackgroundService { get; init; }

            public int MaxSchedulerConcurrency { get; init; }

            public bool ConnectionPooling { get; init; }

            public JobSection()
            {
                // Mockable support
            }

            public JobSection(IConfiguration config)
            {
                Enabled = config.Required<bool>("Job:Enabled");
                BackgroundJobStartDelaySeconds = config.Required<int>("Job:BackgroundJobStartDelaySeconds");
                EnableJobBackgroundService = config.Required<bool>("Job:EnableJobBackgroundService");
                CronProcessingInterval = config.Required<int>("Job:CronProcessingInterval");
                CronBatchSize = config.Required<int>("Job:CronBatchSize");
                EnsureCertificateProcessorIntervalSeconds = config.Required<int>("Job:EnsureCertificateProcessorIntervalSeconds");
                ProcessPendingCertificateOrderIntervalInSeconds = config.Required<int>("Job:ProcessPendingCertificateOrderIntervalInSeconds");
                MaxSchedulerConcurrency = config.Required<int>("Job:MaxSchedulerConcurrency");
                ConnectionPooling = config.GetOrDefault("Job:ConnectionPooling", true);
            }
        }

        //

        public class LoggingSection
        {
            public string LogFilePath { get; init; }

            public LoggingSection()
            {
                // Mockable support
            }

            public LoggingSection(IConfiguration config)
            {
                LogFilePath = config.GetOrDefault("Logging:LogFilePath", "");
            }
        }

        //

        public class CertificateRenewalSection
        {
            public CertificateRenewalSection()
            {
                // Mockable support
            }

            public CertificateRenewalSection(IConfiguration config)
            {
                UseCertificateAuthorityProductionServers = config.Required<bool>("CertificateRenewal:UseCertificateAuthorityProductionServers");
                CertificateAuthorityAssociatedEmail = config.Required<string>("CertificateRenewal:CertificateAuthorityAssociatedEmail");
            }

            /// <summary>
            /// Specifies if the production servers of the certificate authority should be used.
            /// </summary>
            public bool UseCertificateAuthorityProductionServers { get; init; }

            /// <summary>
            /// The email addressed given to Certificate Authorities when users ask us to manage their certificates
            /// </summary>
            public string CertificateAuthorityAssociatedEmail { get; init; }

            public CertificateRenewalConfig ToCertificateRenewalConfig()
            {
                return new CertificateRenewalConfig()
                {
                    UseCertificateAuthorityProductionServers = UseCertificateAuthorityProductionServers,
                    CertificateAuthorityAssociatedEmail = CertificateAuthorityAssociatedEmail,
                };
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
                ApiKey = config.Required<string>("Mailgun:ApiKey");
                DefaultFrom = new NameAndEmailAddress
                {
                    Email = config.Required<string>("Mailgun:DefaultFromEmail"),
                    Name = config.GetOrDefault("Mailgun:DefaultFromName", ""),
                };
                EmailDomain = config.Required<string>("Mailgun:EmailDomain");
                Enabled = config.Required<bool>("Mailgun:Enabled");
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
                ApiEnabled = config.Required<bool>("Admin:ApiEnabled");
                ApiKey = config.Required<string>("Admin:ApiKey");
                ApiKeyHttpHeaderName = config.Required<string>("Admin:ApiKeyHttpHeaderName");
                ApiPort = config.Required<int>("Admin:ApiPort");
                Domain = config.Required<string>("Admin:Domain");
                ExportTargetPath = config.Required<string>("Admin:ExportTargetPath");
            }
        }

        //
    }
}