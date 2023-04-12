#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Microsoft.Extensions.Configuration;
using Serilog;
using Youverse.Core.Configuration;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Certificate;
using Youverse.Core.Services.Certificate.Renewal;
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
            public string ProvisioningDomain { get; }
            public List<string> ManagedDomains { get; }

            public string DnsTargetRecordType { get; }

            public string DnsTargetAddress { get; }

            public RegistrySection(IConfiguration config)
            {
                ProvisioningDomain = config.Required<string>("Registry:ProvisioningDomain");
                ManagedDomains = config.Required<List<string>>("Registry:ManagedDomains");
                DnsTargetRecordType = config.Required<string>("Registry:DnsTargetRecordType");
                DnsTargetAddress = config.Required<string>("Registry:DnsTargetAddress");
            }
        }

        public class HostSection
        {
            public string TenantDataRootPath { get; }
            public string SystemDataRootPath { get; }
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
                NumberOfCertificateValidationTries = config.Required<int>("CertificateRenewal:NumberOfCertificateValidationTries");
                UseCertificateAuthorityProductionServers = config.Required<bool>("CertificateRenewal:UseCertificateAuthorityProductionServers");
                CertificateAuthorityAssociatedEmail = config.Required<string>("CertificateRenewal:CertificateAuthorityAssociatedEmail");
                CsrCountryName = config.Required<string>("CertificateRenewal:CsrCountryName");
                CsrState = config.Required<string>("CertificateRenewal:CsrState");
                CsrLocality = config.Required<string>("CertificateRenewal:CsrLocality");
                CsrOrganization = config.Required<string>("CertificateRenewal:CsrOrganization");
                CsrOrganizationUnit = config.Required<string>("CertificateRenewal:CsrOrganizationUnit");
            }

            /// <summary>
            /// The number of times certificate validation should be checked before failing
            /// </summary>
            public int NumberOfCertificateValidationTries { get; }

            /// <summary>
            /// Specifies if the production servers of the certificate authority should be used.
            /// </summary>
            public bool UseCertificateAuthorityProductionServers { get; }

            /// <summary>
            /// The email addressed given to Certificate Authorities when users ask us to manage their certificates
            /// </summary>
            public string CertificateAuthorityAssociatedEmail { get; }

            /// <summary>
            /// Gets or sets the two-letter ISO abbreviation for your country.
            /// </summary>
            public string CsrCountryName { get; }

            /// <summary>
            /// Gets or sets the state or province where your organization is located. Can not be abbreviated.
            /// </summary>
            public string CsrState { get; }

            /// <summary>
            /// Gets or sets the city where your organization is located.
            /// </summary>
            public string CsrLocality { get; }

            /// <summary>
            /// Gets or sets the exact legal name of your organization. Do not abbreviate.
            /// </summary>
            public string CsrOrganization { get; }

            /// <summary>
            /// Gets or sets the optional organizational information.
            /// </summary>
            public string CsrOrganizationUnit { get; }

            public CertificateRenewalConfig ToCertificateRenewalConfig()
            {
                return new CertificateRenewalConfig()
                {
                    UseCertificateAuthorityProductionServers = UseCertificateAuthorityProductionServers,
                    CertificateAuthorityAssociatedEmail = CertificateAuthorityAssociatedEmail,
                    NumberOfCertificateValidationTries = NumberOfCertificateValidationTries,
                    CertificateSigningRequest = new CertificateSigningRequest()
                    {
                        CountryName = CsrCountryName,
                        State = CsrState,
                        Locality = CsrLocality,
                        Organization = CsrOrganization,
                        OrganizationUnit = CsrOrganizationUnit
                    }
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