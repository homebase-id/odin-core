using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Certificate;
using Odin.Services.Registry.Registration;

namespace Odin.Services.Registry
{
    /// <summary>
    /// The invitation code an identity signed up with (#1908)
    /// </summary>
    /// <param name="InvitedBy">The code's owner, if it has one; null for a configured code</param>
    /// <param name="Source">Where the code came from, as the invitation-code service has it</param>
    public record RegistrationInvitation(string Code, string InvitedBy, string Source);

    /// <summary>
    /// An identity's plan and what it gives: its quota (null for the host's default) and public web presence
    /// </summary>
    public record PlanSetting(string PlanId, long? QuotaBytes, bool EnablePublicWebPresence);

    public class IdentityRegistrationRequest
    {
        public Guid? Id { get; init; }
        public OdinId OdinId { get; set; }
        public string Email { get; set; }
        
        /// <summary>
        /// The hosting PlanId as defined by the hosting company.  This is a string of text that
        /// gets stored with the identity   
        /// </summary>
        public string PlanId { get; set; }

        /// <summary>
        /// The plan's quota; null for the host's default (HostingProvider:DefaultQuotaBytes)
        /// </summary>
        public long? QuotaBytes { get; set; }

        public bool IsCertificateManaged { get; set; }

        /// <summary>
        /// Whether the identity is allowed a public home page
        /// </summary>
        public bool EnablePublicWebPresence { get; set; } = true;

        /// <summary>
        /// The invitation code the identity signed up with, if any
        /// </summary>
        public RegistrationInvitation Invitation { get; set; }

        /// <summary>
        /// Optional certificates to match the <see cref="OdinId"/>
        /// </summary>
        public CertificatePemContent OptionalCertificatePemContent { get; set; }
    }
    
    public class IdentityRegistration
    {
        private string _primaryDomainName;

        public Guid Id { get; set; }
        public string Email { get; set; }


        public string PrimaryDomainName
        {
            get => _primaryDomainName;
            set
            {
                _primaryDomainName = value.ToLower();
            }
        }
        
        /// <summary>
        /// Specifies the user requested the DI host manage the certificate
        /// </summary>
        public bool IsCertificateManaged { get; set; }

        /// <summary>
        /// A random id linking the registration process to this IdentityRegistration
        /// indicating the bearer was the one who registered the token
        /// </summary>
        public Guid? FirstRunToken { get; set; }

        /// <summary>
        /// The hosting provider's plan, a label (docs/hosting-plans.md). What it gives is <see cref="QuotaBytes"/>
        /// and <see cref="EnablePublicWebPresence"/>, set with it.
        /// </summary>
        public string PlanId { get; set; }

        /// <summary>
        /// Bytes of payloads the identity may store; null for the host's HostingProvider:DefaultQuotaBytes
        /// </summary>
        public long? QuotaBytes { get; set; }
        
        /// <summary>
        /// Operational state of the identity, shared by every node
        /// </summary>
        public TenantStatus Status { get; set; } = TenantStatus.Active;

        /// <summary>
        /// Why the identity is disabled; only set when <see cref="Status"/> is <see cref="TenantStatus.Disabled"/>
        /// </summary>
        public DisabledReason? DisabledReason { get; set; }

        /// <summary>
        /// When <see cref="Status"/> last changed, if known
        /// </summary>
        public UnixTimeUtc? StatusChangedAt { get; set; }

        /// <summary>
        /// <see cref="Status"/>, <see cref="DisabledReason"/> and <see cref="StatusChangedAt"/> as one value
        /// </summary>
        public TenantStatusState StatusState => new(Status, DisabledReason, StatusChangedAt);

        /// <summary>
        /// Whether the identity is allowed a public home page (link previews, SEO/SSR content, etc.)
        /// </summary>
        public bool EnablePublicWebPresence { get; set; } = true;

        /// <summary>
        /// The invitation code the identity signed up with, if any. Kept in the json column.
        /// </summary>
        public RegistrationInvitation Invitation { get; set; }

        /// <summary>
        /// The date this account was marked for deletion.
        /// </summary>
        public UnixTimeUtc? MarkedForDeletionDate { get; set; }

        /// <summary>
        /// Timestamp of this account's last seen activity
        /// </summary>
        public UnixTimeUtc? LastSeen { get; set; }

        /// <summary>
        /// When the registration row was created. Set by the database on insert.
        /// </summary>
        public UnixTimeUtc? Created { get; set; }

        public override string ToString()
        {
            return PrimaryDomainName;
        }

        public string[] GetSans()
        {
            var result = new List<string>();
            foreach (var prefix in DnsConfigurationSet.WellknownPrefixes)
            {
                result.Add(prefix + "." + PrimaryDomainName);
            }
            return result.ToArray();
        }
    }
}