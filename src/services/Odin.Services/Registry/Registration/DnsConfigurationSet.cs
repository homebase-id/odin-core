#nullable enable
using Odin.Core.Util;

namespace Odin.Services.Registry.Registration;

public class DnsConfigurationSet
{
    public const string PrefixCertApi = "capi";
    public const string PrefixFile = "file";
    public static readonly string[] WellknownPrefixes = { PrefixCertApi, PrefixFile };

    public string ApexARecord { get; } // SEB:NOTE we currently only allow one A record
    public string ApexAliasRecord { get; }
    
    //
    
    public DnsConfigurationSet(
        string apexARecord,
        string apexAliasRecord)
    {
        ApexARecord = apexARecord;
        ApexAliasRecord = apexAliasRecord;
        AsciiDomainNameValidator.AssertValidDomain(ApexAliasRecord);
    }

    //
    
}