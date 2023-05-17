﻿#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Microsoft.Extensions.Configuration;
using Youverse.Core.Configuration;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Registry.Registration;
using Youverse.Core.Util;

namespace Youverse.Core.Services.Configuration
{
    public class YouverseConfiguration
    {
        public HostSection Host { get; }

        public RegistrySection Registry { get; }
        public DevelopmentSection? Development { get; }

        public LoggingSection Logging { get; }
        public QuartzSection Quartz { get; }
        public CertificateRenewalSection CertificateRenewal { get; set; }

        public FeedSection Feed { get; }
        public TransitSection Transit { get; }

        public YouverseConfiguration(IConfiguration config)
        {
            Host = new HostSection(config);
            Logging = new LoggingSection(config);
            Quartz = new QuartzSection(config);
            Registry = new RegistrySection(config);

            Feed = new FeedSection(config);
            Transit = new TransitSection(config);

            if (config.GetSection("Development") != null)
            {
                Development = new DevelopmentSection(config);
            }

            CertificateRenewal = new CertificateRenewalSection(config);
        }

        //

        public class TransitSection
        {
            public TransitSection(IConfiguration config)
            {
                OutboxBatchSize = config.Required<int>($"Transit:{nameof(OutboxBatchSize)}");

                if (OutboxBatchSize <= 0)
                {
                    throw new YouverseSystemException($"{nameof(OutboxBatchSize)} must be greater than 0");
                }
            }

            public int OutboxBatchSize { get; set; }
        }

        public class FeedSection
        {
            public FeedSection(IConfiguration config)
            {
                InstantDistribution = config.Required<bool>("Feed:InstantDistribution");
                DistributionBatchSize = config.Required<int>("Feed:DistributionBatchSize");

                if (DistributionBatchSize <= 0)
                {
                    throw new YouverseSystemException($"{nameof(DistributionBatchSize)} must be greater than 0");
                }
            }

            public int DistributionBatchSize { get; set; }

            /// <summary>
            /// If true, the feed files are sent immediately to all
            /// recipients; This should be false in high traffic environments
            /// </summary>
            public bool InstantDistribution { get; }
        }

        /// <summary>
        /// Settings specific to the development/demo process
        /// </summary>
        public class DevelopmentSection
        {
            public DevelopmentSection(IConfiguration config)
            {
                PreconfiguredDomains = config.Required<List<string>>("Development:PreconfiguredDomains");
                SslSourcePath = config.Required<string>("Development:SslSourcePath");
            }

            public List<string> PreconfiguredDomains { get; }
            public string SslSourcePath { get; }
        }

        public class RegistrySection
        {
            public virtual string PowerDnsHostAddress { get; }
            public virtual string PowerDnsApiKey { get; }

            public string ProvisioningDomain { get; }
            public List<ManagedDomainApex> ManagedDomainApexes { get; }

            public DnsConfigurationSet DnsConfigurationSet { get; }
            public List<string> DnsResolvers { get; }

            public RegistrySection(IConfiguration config)
            {
                PowerDnsHostAddress = config.Required<string>("Registry:PowerDnsHostAddress");
                PowerDnsApiKey = config.Required<string>("Registry:PowerDnsApiKey");
                ProvisioningDomain = config.Required<string>("Registry:ProvisioningDomain");
                ManagedDomainApexes = config.Required<List<ManagedDomainApex>>("Registry:ManagedDomainApexes");
                DnsResolvers = config.Required<List<string>>("Registry:DnsResolvers");
                DnsConfigurationSet = new DnsConfigurationSet(
                    config.Required<List<string>>("Registry:DnsRecordValues:BareARecords"),
                    config.GetOrDefault<string>("Registry:DnsRecordValues:WwwCnameTarget", ""),
                    config.GetOrDefault<string>("Registry:DnsRecordValues:ApiCnameTarget", ""),
                    config.GetOrDefault<string>("Registry:DnsRecordValues:CApiCnameTarget", ""),
                    config.GetOrDefault<string>("Registry:DnsRecordValues:FileCnameTarget", ""));
            }

            public class ManagedDomainApex
            {
                public string Apex { get; set; } = "";
                public List<string> PrefixLabels { get; set; } = new();
            }
        }

        public class HostSection
        {
            public string TenantDataRootPath { get; }
            public string SystemDataRootPath { get; }
            
            public string? AlternativePayloadPath { get; }
            
            public string SystemSslRootPath { get; }

            /// <summary>
            /// List of IPv4 or IPv6 IP address on which to listen 
            /// </summary>
            public List<ListenEntry> IPAddressListenList { get; }

            public int CacheSlidingExpirationSeconds { get; }

            public HostSection(IConfiguration config)
            {
                var isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
                var home = Environment.GetEnvironmentVariable("HOME") ?? "";

                var p = config.Required<string>("Host:TenantDataRootPath");
                TenantDataRootPath = isDev && !p.StartsWith(home) ? PathUtil.Combine(home, p.Substring(1)) : p;

                var altPayloadPath = config.GetOrDefault<string>("Host:PayloadAlternativePath", string.Empty);
                if (!string.IsNullOrEmpty(altPayloadPath))
                {
                    AlternativePayloadPath = isDev && !p.StartsWith(home) ? PathUtil.Combine(home, altPayloadPath.Substring(1)) : altPayloadPath;
                }

                var sd = config.Required<string>("Host:SystemDataRootPath");
                SystemDataRootPath = isDev && !p.StartsWith(home) ? PathUtil.Combine(home, sd.Substring(1)) : sd;

                SystemSslRootPath = Path.Combine(SystemDataRootPath, "ssl");

                IPAddressListenList = config.Required<List<ListenEntry>>("Host:IPAddressListenList");

                CacheSlidingExpirationSeconds = config.Required<int>("Host:CacheSlidingExpirationSeconds");
            }
        }

        public class ListenEntry
        {
            public string Ip { get; set; } = "";
            public int HttpsPort { get; set; } = 0;
            public int HttpPort { get; set; } = 0;

            public IPAddress GetIp()
            {
                return IPAddress.Parse(this.Ip);
            }
        }


        //

        public class QuartzSection
        {
            /// <summary>
            /// Number of seconds to delay starting background jobs when starting the dotyoucore process
            /// </summary>
            public int BackgroundJobStartDelaySeconds { get; }

            public int CronProcessingInterval { get; }

            public int EnsureCertificateProcessorIntervalSeconds { get; }

            /// <summary>
            /// The interval in which we check for the validation of certificate order
            /// </summary>
            public int ProcessPendingCertificateOrderIntervalInSeconds { get; }

            /// <summary>
            ///  The number of items to query from the cron queue each time the job runs 
            /// </summary>
            public int CronBatchSize { get; }

            public bool EnableQuartzBackgroundService { get; }

            public QuartzSection(IConfiguration config)
            {
                BackgroundJobStartDelaySeconds = config.Required<int>("Quartz:BackgroundJobStartDelaySeconds");
                EnableQuartzBackgroundService = config.Required<bool>("Quartz:EnableQuartzBackgroundService");
                CronProcessingInterval = config.Required<int>("Quartz:CronProcessingInterval");
                CronBatchSize = config.Required<int>("Quartz:CronBatchSize");
                EnsureCertificateProcessorIntervalSeconds = config.Required<int>("Quartz:EnsureCertificateProcessorIntervalSeconds");
                ProcessPendingCertificateOrderIntervalInSeconds = config.Required<int>("Quartz:ProcessPendingCertificateOrderIntervalInSeconds");
            }
        }

        //

        public class LoggingSection
        {
            public string LogFilePath { get; }

            public LoggingLevel Level { get; }

            public LoggingSection(IConfiguration config)
            {
                LogFilePath = config.Required<string>("Logging:LogFilePath");
                Level = Enum.Parse<LoggingLevel>(config.Required<string>("Logging:Level"));
            }
        }

        //

        public class CertificateRenewalSection
        {
            public CertificateRenewalSection(IConfiguration config)
            {
                UseCertificateAuthorityProductionServers = config.Required<bool>("CertificateRenewal:UseCertificateAuthorityProductionServers");
                CertificateAuthorityAssociatedEmail = config.Required<string>("CertificateRenewal:CertificateAuthorityAssociatedEmail");
            }

            /// <summary>
            /// Specifies if the production servers of the certificate authority should be used.
            /// </summary>
            public bool UseCertificateAuthorityProductionServers { get; }

            /// <summary>
            /// The email addressed given to Certificate Authorities when users ask us to manage their certificates
            /// </summary>
            public string CertificateAuthorityAssociatedEmail { get; }

            public CertificateRenewalConfig ToCertificateRenewalConfig()
            {
                return new CertificateRenewalConfig()
                {
                    UseCertificateAuthorityProductionServers = UseCertificateAuthorityProductionServers,
                    CertificateAuthorityAssociatedEmail = CertificateAuthorityAssociatedEmail,
                };
            }
        }
    }

    public enum LoggingLevel
    {
        Verbose,
        ErrorsOnly
    }
}