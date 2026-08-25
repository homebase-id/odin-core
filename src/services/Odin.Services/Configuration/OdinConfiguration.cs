using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Configuration;
using Odin.Core.Configuration;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Cache;
using Odin.Core.Storage.Factory;
using Odin.Core.Util;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Email;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Configuration;

#nullable enable

public class OdinConfiguration
{
    public HostSection Host { get; init; } = new();

    public RegistrySection Registry { get; init; } = new();

    public AccountRecoverySection AccountRecovery { get; init; } = new();

    public DevelopmentSection Development { get; init; } = new();

    public LoggingSection Logging { get; init; } = new();
    public BackgroundServicesSection BackgroundServices { get; init; } = new();
    public CertificateRenewalSection CertificateRenewal { get; init; } = new();

    public EmailSection Email { get; init; } = new();
    public AdminSection Admin { get; init; } = new();

    public FeedSection Feed { get; init; } = new();

    public PushNotificationSection PushNotification { get; init; } = new();
    public DatabaseSection Database { get; init; } = new();

    public RedisSection Redis { get; init; } = new();
    public CacheSection Cache { get; init; } = new();

    public S3StorageSection S3Storage { get; init; } = new();
    public S3PayloadSection S3Payload { get; init; } = new();

    public CdnSection Cdn { get; init; } = new();

    public OpenObserveSection OpenObserve { get; init; } = new();

    public OdinConfiguration()
    {
        // Mockable support
    }

    //

    public OdinConfiguration(IConfiguration config)
    {
        Host = new HostSection(config);
        Logging = new LoggingSection(config);
        BackgroundServices = new BackgroundServicesSection(config);
        Registry = new RegistrySection(config);
        Email = new EmailSection(config);
        Admin = new AdminSection(config);
        AccountRecovery = new AccountRecoverySection(config);
        Development = new DevelopmentSection(config);
        Feed = new FeedSection(config);
        CertificateRenewal = new CertificateRenewalSection(config);
        PushNotification = new PushNotificationSection(config);
        Database = new DatabaseSection(config);
        Redis = new RedisSection(config);
        Cache = new CacheSection(config);
        S3Storage = new S3StorageSection(config);
        S3Payload = new S3PayloadSection(config);
        Cdn = new CdnSection(config);
        OpenObserve = new OpenObserveSection(config);
    }

    //

    public class FeedSection
    {
        public int MaxCommentsInPreview { get; init; }

        public FeedSection()
        {
            // Mockable support
        }

        public FeedSection(IConfiguration config)
        {
            MaxCommentsInPreview = config.GetOrDefault("Feed:MaxCommentsInPreview", 3);
        }
    }

    //

    public class AccountRecoverySection
    {
        public bool Enabled { get; init; }
        public Guid AutomatedIdentityKey { get; init; }

        /// <summary>
        /// The identities to use when users enable automated password recovery
        /// </summary>
        public List<string> AutomatedPasswordRecoveryIdentities { get; init; } = [];

        public AccountRecoverySection()
        {
            // Mockable support
        }

        public AccountRecoverySection(IConfiguration config)
        {
            Enabled = config.GetOrDefault("AccountRecovery:Enabled", false);
            if (Enabled)
            {
                AutomatedIdentityKey = config.Required<Guid>("AccountRecovery:AutomatedIdentityKey");
                AutomatedPasswordRecoveryIdentities = config.Required<List<string>>("AccountRecovery:AutomatedPasswordRecoveryIdentities");
            }
        }
    }

    /// <summary>
    /// Settings specific to the development/demo process
    /// </summary>
    public class DevelopmentSection
    {
        public bool Enabled { get; }
        public List<string> PreconfiguredDomains { get; init; } = [];
        public string SslSourcePath { get; init; } = "";
        public bool VersionUpgradeTestModeEnabled { get; init; }

        public DevelopmentSection()
        {
            // Mockable support
        }

        public DevelopmentSection(IConfiguration config)
        {
            Enabled = config.SectionExists("Development");
            if (Enabled)
            {
                PreconfiguredDomains = config.GetOrDefault("Development:PreconfiguredDomains", PreconfiguredDomains).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                SslSourcePath = config.Required<string>("Development:SslSourcePath");
                VersionUpgradeTestModeEnabled = config.GetOrDefault("Development:VersionUpgradeTestModeEnabled", false);
            }
        }
    }

    //

    public class RegistrySection
    {
        /// <summary>
        /// Invitation codes that register an identity with a public web presence
        /// </summary>
        public List<string> InvitationCodes { get; init; } = [];

        /// <summary>
        /// Invitation codes that register an identity without a public web presence
        /// </summary>
        public List<string> InvitationCodesWithoutPublicWebPresence { get; init; } = [];

        public string PowerDnsHostAddress { get; init; } = "";
        public string PowerDnsApiKey { get; set; } = "";

        public string ProvisioningDomain { get; init; } = "";
        public bool ProvisioningEnabled { get; init; }

        public List<ManagedDomainApex> ManagedDomainApexes { get; init; } = [];

        /// <summary>
        /// Whether the post-startup DNS infrastructure check runs (<see cref="Odin.Services.Dns.Health.DnsInfraVerifier"/>).
        /// Default true; set false where the configured hostnames are not the real ones — a dev box
        /// resolves them through /etc/hosts, so every lookup fails and the retry loop just logs noise.
        /// Not a domain allowlist on purpose: the infra domain is also the production one, so
        /// skipping it by name would disable the check exactly where it is wanted.
        /// </summary>
        public bool DnsInfraVerificationEnabled { get; init; } = true;

        public DnsConfigurationSet DnsConfigurationSet { get; init; } = new("127.0.0.1", "example.com");
        public List<string> DnsResolvers { get; init; } = [];
        public long DaysUntilAccountDeletion { get; init; } = long.MaxValue;

        public RegistrySection()
        {
            // Mockable support
        }

        public RegistrySection(IConfiguration config)
        {
            PowerDnsHostAddress = config.GetOrDefault("Registry:PowerDnsHostAddress", "localhost");
            PowerDnsApiKey = config.GetOrDefault("Registry:PowerDnsApiKey", "");
            ProvisioningDomain = config.Required<string>("Registry:ProvisioningDomain").Trim().ToLower();
            ProvisioningEnabled = config.GetOrDefault("Registry:ProvisioningEnabled", true);
            AsciiDomainNameValidator.AssertValidDomain(ProvisioningDomain);
            ManagedDomainApexes = config.GetOrDefault("Registry:ManagedDomainApexes", ManagedDomainApexes);
            DnsInfraVerificationEnabled = config.GetOrDefault("Registry:DnsInfraVerificationEnabled", true);
            DnsResolvers = config.GetOrDefault("Registry:DnsResolvers",
                new List<string> { "1.1.1.1", "8.8.8.8", "9.9.9.9", "208.67.222.222" });
            DnsConfigurationSet = new DnsConfigurationSet(
                config.Required<List<string>>("Registry:DnsRecordValues:ApexARecords")
                    .First(), // SEB:NOTE we currently only allow one A record
                config.Required<string>("Registry:DnsRecordValues:ApexAliasRecord"),
                // Deliberately no default nameservers: a deployment that runs its own PowerDNS
                // but doesn't configure NameServers must NOT instruct its users to delegate to
                // somebody else's infrastructure. NS delegation is opt-in via config.
                config.GetOrDefault("Registry:DnsRecordValues:NameServers", new List<string>()),
                config.GetOrDefault("Registry:DnsRecordValues:SoaAdminEmail", ""));
            InvitationCodes = config.GetOrDefault("Registry:InvitationCodes", InvitationCodes);
            InvitationCodesWithoutPublicWebPresence = config.GetOrDefault(
                "Registry:InvitationCodesWithoutPublicWebPresence", InvitationCodesWithoutPublicWebPresence);
            DaysUntilAccountDeletion = config.GetOrDefault("Registry:DaysUntilAccountDeletion", 30);

            var ambiguousCodes = InvitationCodes
                .Intersect(InvitationCodesWithoutPublicWebPresence, StringComparer.InvariantCultureIgnoreCase)
                .ToList();
            if (ambiguousCodes.Count > 0)
            {
                throw new OdinSystemException(
                    "Registry:InvitationCodes and Registry:InvitationCodesWithoutPublicWebPresence must not overlap: " +
                    string.Join(", ", ambiguousCodes));
            }
        }

        public class ManagedDomainApex
        {
            public string Apex { get; init; } = "";
            public List<string> PrefixLabels { get; init; } = [];
        }
    }

    //

    public class HostSection
    {
        public string TenantDataRootPath { get; init; } = "";
        public string SystemDataRootPath { get; init; } = "";
        public string DataProtectionKeyPath { get; init; } = "";
        public bool Http1Only { get; init; }

        public int ClientRegistrationThreshold { get; init; }
        public int ClientRegistrationWindowThreshold { get; init; }

        /// <summary>
        /// List of IPv4 or IPv6 IP address on which to listen
        /// </summary>
        public List<ListenEntry> IpAddressListenList { get; } = [];

        public int ShutdownTimeoutSeconds { get; init; }
        public Guid SystemProcessApiKey { get; set; }

        public int IpRateLimitRequestsPerSecond { get; init; }

        public string ReportContentUrl { get; set; } = "";

        public int DefaultHttpPort => IpAddressListenList.FirstOrDefault()?.HttpPort ?? 80;
        public int DefaultHttpsPort => IpAddressListenList.FirstOrDefault()?.HttpsPort ?? 443;
        public int HomePageCachingExpirationSeconds { get; set; }
        public string PushNotificationSubject { get; set; } = "";

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
        public int PeerOperationMaxAttempts { get; init; }
        public int OutboxOperationMaxAttempts { get; init; }

        public TimeSpan PeerOperationDelayMs { get; init; }

        /// <summary>
        /// The age in seconds of items that should be recovered which have been
        /// popped (checked out) of the inbox/outbox queue w/o having been marked complete or failed
        /// </summary>
        public int InboxOutboxRecoveryAgeSeconds { get; init; }

        public TimeSpan CapiSessionLifetime { get; init; }

        public HostSection()
        {
            // Mockable support
        }

        public HostSection(IConfiguration config)
        {
            TenantDataRootPath = Env.ExpandEnvironmentVariablesCrossPlatform(config.Required<string>("Host:TenantDataRootPath"));

            SystemDataRootPath = Env.ExpandEnvironmentVariablesCrossPlatform(config.Required<string>("Host:SystemDataRootPath"));

            DataProtectionKeyPath = Path.Combine(SystemDataRootPath, "asp-data-protection-keys");

            Http1Only = config.GetOrDefault("Host:Http1Only", false);

            IpAddressListenList = config.Required<List<ListenEntry>>("Host:IPAddressListenList");

            HomePageCachingExpirationSeconds = config.GetOrDefault("Host:HomePageCachingExpirationSeconds", 5 * 60);

            ShutdownTimeoutSeconds = config.GetOrDefault("Host:ShutdownTimeoutSeconds", 120);
            SystemProcessApiKey = config.GetOrDefault("Host:SystemProcessApiKey", Guid.NewGuid());

            PushNotificationSubject = config.GetOrDefault("Host:PushNotificationSubject", "mailto:info@homebase.id");
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

            ClientRegistrationThreshold = config.GetOrDefault("Host:ClientRegistrationThreshold", 10);
            ClientRegistrationWindowThreshold = config.GetOrDefault("Host:ClientRegistrationWindowThreshold", 3);

            CapiSessionLifetime = TimeSpan.FromMinutes(config.GetOrDefault("Host:CapiSessionLifetimeMinutes", 10));
            if (CapiSessionLifetime < TimeSpan.FromMinutes(1))
            {
                throw new OdinConfigException("Invalid CapiSessionLifetime");
            }
        }
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
        public bool SystemBackgroundServicesEnabled { get; set; }
        public bool TenantBackgroundServicesEnabled { get; set; }

        public BackgroundServicesSection()
        {
            // Mockable support
        }

        public BackgroundServicesSection(IConfiguration config)
        {
            EnsureCertificateProcessorIntervalSeconds =
                config.Required<int>("BackgroundServices:EnsureCertificateProcessorIntervalSeconds");
            InboxOutboxReconciliationIntervalSeconds =
                config.Required<int>("BackgroundServices:InboxOutboxReconciliationIntervalSeconds");
            JobCleanUpIntervalSeconds = config.Required<int>("BackgroundServices:JobCleanUpIntervalSeconds");
            SystemBackgroundServicesEnabled = config.GetOrDefault("BackgroundServices:SystemBackgroundServicesEnabled", true);
            TenantBackgroundServicesEnabled = config.GetOrDefault("BackgroundServices:TenantBackgroundServicesEnabled", true);
        }
    }

    //

    public class LoggingSection
    {
        public string LogFilePath { get; init; } = "";
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
        public string CertificateAuthorityAssociatedEmail { get; init; } = "";
        public byte[] StorageKey { get; init; } = [];

        public CertificateRenewalSection()
        {
            // Mockable support
        }

        public CertificateRenewalSection(IConfiguration config)
        {
            UseCertificateAuthorityProductionServers =
                config.Required<bool>("CertificateRenewal:UseCertificateAuthorityProductionServers");
            CertificateAuthorityAssociatedEmail = config.Required<string>("CertificateRenewal:CertificateAuthorityAssociatedEmail");
            StorageKey = Convert.FromHexString(config.Required<string>("CertificateRenewal:StorageKey"));
            if (StorageKey.Length != 32)
            {
                throw new OdinConfigException("CertificateRenewal:StorageKey must be a 32-byte hex string");
            }
        }
    }

    //

    public class EmailSection
    {
        public EmailProvider Provider { get; init; } = EmailProvider.None;
        public NameAndEmailAddress SystemFrom { get; init; } = new();
        public SendGridProviderSection SendGrid { get; init; } = new();
        public MailgunProviderSection Mailgun { get; init; } = new();
        public SmtpProviderSection Smtp { get; init; } = new();
        public TenantMailSection TenantMail { get; init; } = new();
        public StalwartSection Stalwart { get; init; } = new();

        /// <summary>
        /// True when the system sender's credentials came from the deprecated TOP-LEVEL
        /// <c>Mailgun:</c> section rather than from <c>Email:Provider</c> + <c>Email:Mailgun:*</c>.
        ///
        /// This is about WHERE the settings live, not whether the feature is wanted: sending our
        /// own mail through Mailgun is current and expected. Only its address in the config file
        /// moved. Hence the startup log says "move it", not "stop using it".
        /// </summary>
        public bool UsingDeprecatedMailgunSection { get; init; }

        /// <summary>
        /// AES key encrypting tenant DKIM private keys at rest (DkimStore) - the
        /// CertificateRenewal:StorageKey pattern, as a separate key by hygiene.
        /// Optional until email activation ships to an environment: empty means
        /// the DkimStore refuses to operate, nothing else is affected.
        /// </summary>
        public byte[] DkimStorageKey { get; init; } = [];

        /// <summary>
        /// Gates policy and scheduling decisions (recovery mode, email jobs) - the
        /// replacement for the old Mailgun.Enabled flag. With Provider "None" an
        /// IEmailSender still resolves (NullEmailSender), but nothing should rely
        /// on reaching it.
        /// </summary>
        public bool IsProviderConfigured => Provider != EmailProvider.None;

        public EmailSection()
        {
            // Mockable support
        }

        public EmailSection(IConfiguration config)
        {
            // True only when the deprecated section actually supplied the values, which is what
            // the startup deprecation warning claims ("in use"). A leftover Mailgun block that
            // Email:Provider has superseded is simply ignored.
            UsingDeprecatedMailgunSection = !config.SectionExists("Email:Provider") && config.SectionExists("Mailgun");

            // The SYSTEM SENDER (this host's own notifications) and TENANT MAIL (mailboxes it
            // serves for identities) are independent, and this parsing keeps them that way.
            //
            // They used to be coupled: the legacy branch early-returned whenever an Email
            // section existed, so the first Email:* key added for tenant mail silently switched
            // the whole deprecated Mailgun section off - no error, no bounce, just no system
            // mail. Enabling tenant mail must not be able to break password recovery.
            //
            // An EXPLICIT Email:Provider always wins, "None" included: that is a deliberate
            // "this host sends no mail of its own". Only an ABSENT key falls back to the
            // deprecated section, which is what lets Email:TenantMail:* be added on its own.
            if (!config.SectionExists("Email:Provider") && config.GetOrDefault("Mailgun:Enabled", false))
            {
                Provider = EmailProvider.Mailgun;
                Mailgun = new MailgunProviderSection
                {
                    ApiKey = config.Required<string>("Mailgun:ApiKey"),
                    EmailDomain = config.Required<string>("Mailgun:EmailDomain"),
                };
                SystemFrom = new NameAndEmailAddress
                {
                    Email = config.Required<string>("Mailgun:DefaultFromEmail"),
                    Name = config.GetOrDefault("Mailgun:DefaultFromName", ""),
                };
            }

            if (config.SectionExists("Email:Provider"))
            {
                Provider = config.GetOrDefault("Email:Provider", EmailProvider.None);
                if (Provider != EmailProvider.None)
                {
                    SystemFrom = new NameAndEmailAddress
                    {
                        Email = config.Required<string>("Email:SystemFrom:Email"),
                        Name = config.GetOrDefault("Email:SystemFrom:Name", ""),
                    };
                }

                // Only the selected provider's credentials are required
                switch (Provider)
                {
                    case EmailProvider.SendGrid:
                        SendGrid = new SendGridProviderSection
                        {
                            ApiKey = config.Required<string>("Email:SendGrid:ApiKey"),
                        };
                        break;
                    case EmailProvider.Mailgun:
                        Mailgun = new MailgunProviderSection
                        {
                            ApiKey = config.Required<string>("Email:Mailgun:ApiKey"),
                            EmailDomain = config.Required<string>("Email:Mailgun:EmailDomain"),
                        };
                        break;
                    case EmailProvider.Smtp:
                        Smtp = new SmtpProviderSection
                        {
                            RelayHost = config.Required<string>("Email:Smtp:RelayHost"),
                            RelayPort = config.GetOrDefault("Email:Smtp:RelayPort", 25),
                            Username = config.GetOrDefault("Email:Smtp:Username", ""),
                            Password = config.GetOrDefault("Email:Smtp:Password", ""),
                            RequireTls = config.GetOrDefault("Email:Smtp:RequireTls", false),
                            LocalDomain = config.GetOrDefault("Email:Smtp:LocalDomain", ""),
                            RelayIps = config.GetOrDefault("Email:Smtp:RelayIps", new List<string>()),
                        };
                        break;
                }
            }

            TenantMail = new TenantMailSection(config);

            var dkimStorageKeyHex = config.GetOrDefault("Email:DkimStorageKey", "");
            if (!string.IsNullOrEmpty(dkimStorageKeyHex))
            {
                DkimStorageKey = Convert.FromHexString(dkimStorageKeyHex);
                if (DkimStorageKey.Length != 32)
                {
                    throw new OdinConfigException("Email:DkimStorageKey must be a 32-byte hex string");
                }
            }

            Stalwart = new StalwartSection(config);
        }
    }

    /// <summary>
    /// The Stalwart mail-server management endpoint (docs/email-keys-plan.md "The
    /// Stalwart wrapper"). Absent = NullMailboxProvider; present = the real provider.
    /// One endpoint per host group.
    /// </summary>
    public class StalwartSection
    {
        /// <summary>Management base URL, e.g. "http://localhost:9080" - the /jmap endpoint lives under it.</summary>
        public string BaseUrl { get; init; } = "";

        public string AdminUsername { get; init; } = "";
        public string AdminPassword { get; init; } = "";

        public bool IsConfigured => !string.IsNullOrEmpty(BaseUrl);

        public StalwartSection()
        {
            // Mockable support
        }

        public StalwartSection(IConfiguration config)
        {
            BaseUrl = config.GetOrDefault("Email:Stalwart:BaseUrl", "").TrimEnd('/');
            if (IsConfigured)
            {
                AdminUsername = config.Required<string>("Email:Stalwart:AdminUsername");
                AdminPassword = config.Required<string>("Email:Stalwart:AdminPassword");
            }
        }
    }

    public class SendGridProviderSection
    {
        public string ApiKey { get; init; } = "";
    }

    public class MailgunProviderSection
    {
        public string ApiKey { get; init; } = "";
        public string EmailDomain { get; init; } = "";
    }

    /// <summary>
    /// Submission into the host's own mail server, which DKIM-signs and relays onward
    /// (docs/email-keys-plan.md: "Homebase send API -> submits into Stalwart -> Stalwart
    /// DKIM-signs -> relay"). Homebase never signs or relays outbound mail itself.
    /// </summary>
    public class SmtpProviderSection
    {
        /// <summary>The mail server to submit to — locally, the Stalwart container.</summary>
        public string RelayHost { get; init; } = "";

        /// <summary>
        /// Submission port. 25 suits a mail server that accepts loopback submission for its own
        /// domains; 587 is the authenticated submission port and needs credentials below.
        /// </summary>
        public int RelayPort { get; init; } = 25;

        /// <summary>Optional submission credentials. Omit for an unauthenticated local relay.</summary>
        public string Username { get; init; } = "";

        public string Password { get; init; } = "";

        /// <summary>
        /// Whether to require TLS. Off by default because the usual deployment submits over
        /// loopback to a mail server on the same host, where STARTTLS buys nothing and a
        /// self-signed dev certificate would just fail the connection.
        /// </summary>
        public bool RequireTls { get; init; }

        /// <summary>
        /// The name announced in EHLO. Mail servers commonly reject a bare, non-FQDN hostname —
        /// Stalwart answers "5.5.0 Invalid EHLO domain" — and the OS hostname of a Homebase host
        /// is rarely its mail name, so this is configured rather than guessed. Empty means "let
        /// the client decide", which is only safe where the machine already has a proper FQDN.
        /// </summary>
        public string LocalDomain { get; init; } = "";

        /// <summary>The IPs outbound leaves from; published in SPF for self-sending setups.</summary>
        public List<string> RelayIps { get; init; } = [];
    }

    public class TenantMailSection
    {
        public bool Enabled { get; init; }
        public string CanaryDomain { get; init; } = "";
        public List<string> MxNodes { get; init; } = [];
        public string SpfIncludeTarget { get; init; } = "";
        public string DmarcReportEmail { get; init; } = "";
        public string TlsReportEmail { get; init; } = "";

        public TenantMailSection()
        {
            // Mockable support
        }

        public TenantMailSection(IConfiguration config)
        {
            Enabled = config.GetOrDefault("Email:TenantMail:Enabled", false);
            if (Enabled)
            {
                CanaryDomain = config.GetOrDefault("Email:TenantMail:CanaryDomain", "");
                MxNodes = config.Required<List<string>>("Email:TenantMail:MxNodes");
                SpfIncludeTarget = config.Required<string>("Email:TenantMail:SpfIncludeTarget");
                DmarcReportEmail = config.Required<string>("Email:TenantMail:DmarcReportEmail");
                TlsReportEmail = config.Required<string>("Email:TenantMail:TlsReportEmail");
            }
        }
    }

    //

    public class AdminSection
    {
        public bool ApiEnabled { get; init; }
        public string ApiKey { get; init; } = "";
        public string ApiKeyHttpHeaderName { get; init; } = "";
        public int ApiPort { get; init; }
        public string Domain { get; init; } = "";
        public string ExportTargetPath { get; init; } = "";

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
        public string BaseUrl { get; init; } = "";

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
        public long MemoryCacheSizeLimit { get; init; }
        public double MemoryCacheCompactionPercentage { get; init; }
        public Level2CacheType Level2CacheType { get; init; }

        public CacheSection()
        {
            // Mockable support
        }

        public CacheSection(IConfiguration config)
        {
            var guesstimatedMemoryCacheSizeLimit = EntrySize.GuesstimateMemoryCacheSizeLimit();
            MemoryCacheSizeLimit = config.GetOrDefault("Cache:MemoryCacheSizeLimit", guesstimatedMemoryCacheSizeLimit);
            MemoryCacheCompactionPercentage = config.GetOrDefault("Cache:MemoryCacheSizeLimit", 0.25);
            Level2CacheType = config.GetOrDefault("Cache:Level2CacheType", Level2CacheType.None);
        }
    }

    //

    public class S3StorageSection
    {
        public bool Enabled { get; init; }
        public string AccessKey { get; init; } = "";
        public string SecretAccessKey { get; init; } = "";
        public string ServiceUrl { get; init; } = "";
        public string Region { get; init; } = "";
        public bool ForcePathStyle { get; init; }
        public int RetryAttempts { get; init; } = 5;
        public int RetryInitialBackoffMs { get; init; } = 5000;

        public S3StorageSection()
        {
            // Mockable support
        }

        public S3StorageSection(IConfiguration config)
        {
            Enabled = config.GetOrDefault("S3Storage:Enabled", false);
            if (Enabled)
            {
                AccessKey = config.Required<string>("S3Storage:AccessKey");
                SecretAccessKey = config.Required<string>("S3Storage:SecretAccessKey");
                ServiceUrl = config.Required<string>("S3Storage:ServiceUrl");
                Region = config.GetOrDefault("S3Storage:Region", "");
                ForcePathStyle = config.GetOrDefault("S3Storage:ForcePathStyle", false);
                RetryAttempts = config.GetOrDefault("S3Storage:RetryAttempts", 5);
                RetryInitialBackoffMs = config.GetOrDefault("S3Storage:RetryInitialBackoffMs", 5000);
            }
        }
    }

    //

    public class S3PayloadSection
    {
        public bool Enabled { get; init; }
        public string BucketName { get; init; } = "";
        public string RootPath { get; init; } = "";

        public S3PayloadSection()
        {
            // Mockable support
        }

        public S3PayloadSection(IConfiguration config)
        {
            Enabled = config.GetOrDefault("S3Payload:Enabled", false);
            if (Enabled)
            {
                if (!config.GetOrDefault("S3Storage:Enabled", false))
                {
                    throw new OdinConfigException("S3Storage must be enabled if S3Payload is enabled");
                }
                BucketName = config.Required<string>("S3Payload:BucketName");
                RootPath = config.GetOrDefault("S3Payload:RootPath", "payloads");
            }
        }
    }

    //

    public class CdnSection
    {
        public bool Enabled { get; set; }
        public string PayloadBaseUrl { get; init; } = "";

        public ClientAuthenticationToken ExpectedAuthToken { get; init; } = new();

        public CdnSection()
        {
            // Mockable support
        }

        public CdnSection(IConfiguration config)
        {
            Enabled = config.GetOrDefault("Cdn:Enabled", false);
            if (Enabled)
            {
                PayloadBaseUrl = config.Required<string>("Cdn:PayloadBaseUrl").Trim();
                if (!PayloadBaseUrl.StartsWith("http://") && !PayloadBaseUrl.StartsWith("https://"))
                {
                    throw new OdinConfigException("Cdn:PayloadBaseUrl must begin with http:// or https://");
                }

                if (PayloadBaseUrl.EndsWith('/'))
                {
                    throw new OdinConfigException("Cdn:PayloadBaseUrl must not end with a trailing slash '/'");
                }

                var value = config.Required<string>("Cdn:RequiredAuthToken");
                if (!ClientAuthenticationToken.TryParse(value, out var requiredAuthToken))
                {
                    throw new OdinConfigException("Missing required auth token config.");
                }

                ExpectedAuthToken = requiredAuthToken;
            }
        }
    }

    //

    public class OpenObserveSection
    {
        public bool Enabled { get; init; }
        public string Endpoint { get; init; } = "";
        public string Username { get; init; } = "";
        public string Password { get; init; } = "";
        public string ServiceName { get; init; } = "";

        public OpenObserveSection()
        {
            // Mockable support
        }

        public OpenObserveSection(IConfiguration config)
        {
            Enabled = config.GetOrDefault("OpenObserve:Enabled", false);
            if (Enabled)
            {
                Endpoint = config.Required<string>("OpenObserve:Endpoint");
                Username = config.Required<string>("OpenObserve:Username");
                Password = config.Required<string>("OpenObserve:Password");
                ServiceName = config.Required<string>("OpenObserve:ServiceName");
            }
        }
    }


}