#nullable enable
using System.Net;
using Odin.Core.Configuration;
using Odin.Core.Util;

namespace WaitingListApi.Config
{
    public class WaitingListConfig
    {
        public HostSection Host { get; }

        public LoggingSection Logging { get; }

        public WaitingListConfig(IConfiguration config)
        {
            Host = new HostSection(config);
            Logging = new LoggingSection(config);
        }

        public class HostSection
        {
            public string CorsUrl { get; }
            public string SystemDataRootPath { get; }
            public string SystemSslRootPath { get; }

            /// <summary>
            /// List of IPv4 or IPv6 IP address on which to listen 
            /// </summary>
            public List<ListenEntry> IPAddressListenList { get; }

            public HostSection(IConfiguration config)
            {
                var isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
                var home = Environment.GetEnvironmentVariable("HOME") ?? "";

                CorsUrl = config.Required<string>("Host:CorsUrl");

                var sd = config.Required<string>("Host:SystemDataRootPath");
                SystemDataRootPath = isDev && !sd.StartsWith(home) ? PathUtil.Combine(home, sd.Substring(1)) : sd;

                // var sslPath = config.Required<string>("Host:SystemSslRootPath");
                // SystemSslRootPath = isDev && !sslPath.StartsWith(home) ? PathUtil.Combine(home, sslPath.Substring(1)) : sslPath;

                SystemSslRootPath = config.Required<string>("Host:SystemSslRootPath");

                IPAddressListenList = config.Required<List<ListenEntry>>("Host:IPAddressListenList");
            }
        }

        public class ListenEntry
        {
            public string Ip { get; set; } = "";
            public int HttpsPort { get; set; } = 0;
            public int HttpPort { get; set; } = 0;

            public IPAddress GetIp()
            {
                return this.Ip == "*" ? IPAddress.Any : IPAddress.Parse(this.Ip);
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
    }

    public enum LoggingLevel
    {
        Verbose,
        ErrorsOnly
    }
}