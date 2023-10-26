#nullable enable
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Util;

namespace Odin.Core.Services.Registry.Registration;

public class DnsConfigurationSet
{
    public const string PrefixCertApi = "capi";
    public const string PrefixFile = "file";
    public const string PrefixWww = "www";
    
    public static readonly string[] WellknownPrefixes = { PrefixCertApi, PrefixFile, PrefixWww };

    public string ApexARecord { get; } // SEB:NOTE we currently only allow one A record
    public string ApexAliasRecord { get; }
    public string WwwCnameTarget { get; }
    public string CApiCnameTarget { get; }
    public string FileCnameTarget { get; }
    
    //
    
    public DnsConfigurationSet(
        string apexARecord,
        string apexAliasRecord,
        string wwwCnameTarget, 
        string cApiCnameTarget,
        string fileCnameTarget)
    {
        ApexARecord = apexARecord;
        ApexAliasRecord = apexAliasRecord;
        WwwCnameTarget = wwwCnameTarget;
        CApiCnameTarget = cApiCnameTarget;
        FileCnameTarget = fileCnameTarget;

        AsciiDomainNameValidator.AssertValidDomain(ApexAliasRecord);
    }

    //
    
}