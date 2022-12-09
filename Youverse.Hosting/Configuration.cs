﻿using System;
using Dawn;
using Microsoft.Extensions.Configuration;
using Youverse.Core.Configuration;
using Youverse.Core.Util;

#nullable enable
namespace Youverse.Hosting
{
    public class Configuration
    {
        public HostSection Host { get; }
        public LoggingSection Logging { get; }
        public QuartzSection Quartz { get; }

        public Configuration(IConfiguration config)
        {
            Host = new HostSection(config);
            Logging = new LoggingSection(config);
            Quartz = new QuartzSection(config);
        }

        //

        public class HostSection
        {
            public string RegistryServerUri { get; }
            public string TenantDataRootPath { get; }
            public string TempTenantDataRootPath { get; }
            public bool UseLocalCertificateRegistry { get; }
            public string SystemDataRootPath { get; }

            public HostSection(IConfiguration config)
            {
                var isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
                var home = Environment.GetEnvironmentVariable("HOME") ?? "";

                RegistryServerUri = config.Required<string>("Host:RegistryServerUri");

                var p = config.Required<string>("Host:TenantDataRootPath");
                TenantDataRootPath = isDev && !p.StartsWith(home) ? PathUtil.Combine(home, p.Substring(1)) : p;

                var tp = config.Required<string>("Host:TempTenantDataRootPath");
                TempTenantDataRootPath = isDev && !p.StartsWith(home) ? PathUtil.Combine(home, tp.Substring(1)) : tp;

                var sd = config.Required<string>("Host:SystemDataRootPath");
                SystemDataRootPath = isDev && !p.StartsWith(home) ? PathUtil.Combine(home, sd.Substring(1)) : sd;

                UseLocalCertificateRegistry = config.Required<bool>("Host:UseLocalCertificateRegistry");

                if (UseLocalCertificateRegistry == false)
                {
                    Guard.Argument(Uri.IsWellFormedUriString(RegistryServerUri, UriKind.Absolute), nameof(RegistryServerUri)).True();
                }
            }
        }

        //

        public class QuartzSection
        {
            /// <summary>
            /// Interval in minutes to scan the identity registry to check for certificates that need to be renewed
            /// </summary>
            public int CertificateRenewalCheckIntervalMinutes { get; }

            public int BackgroundJobStartDelaySeconds { get; }

            public int ProcessOutboxIntervalSeconds { get; }
            public bool EnableQuartzBackgroundService { get; }

            public QuartzSection(IConfiguration config)
            {
                BackgroundJobStartDelaySeconds = config.Required<int>("Quartz:BackgroundJobStartDelaySeconds");
                EnableQuartzBackgroundService = config.Required<bool>("Quartz:EnableQuartzBackgroundService");
                ProcessOutboxIntervalSeconds = config.Required<int>("Quartz:ProcessOutboxIntervalSeconds");
            }
        }

        //

        public class LoggingSection
        {
            public LoggingLevel Level { get; }
            public string LogFilePath { get; }

            public LoggingSection(IConfiguration config)
            {
                Level = Enum.Parse<LoggingLevel>(config.Required<string>("Logging:Level"));
                LogFilePath = config.Required<string>("Logging:LogFilePath");
            }
        }

        //
    }

    public enum LoggingLevel
    {
        Verbose,
        ErrorsOnly
    }
}