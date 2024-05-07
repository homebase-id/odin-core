#nullable enable
using System.Collections.Generic;
using System.Linq;
using Odin.Core.Util;

namespace Odin.Services.Registry.Registration;

public class DnsConfigurationSet
{
    public const string PrefixCertApi = "capi";
    public const string PrefixFile = "file";

    public static readonly string[] WellknownPrefixes = { PrefixCertApi, PrefixFile };

    public string ApexARecord { get; } // SEB:NOTE we currently only allow one A record
    public string ApexAliasRecord { get; }
    public string CApiCnameTarget { get; }
    public string FileCnameTarget { get; }
    
    //
    
    public DnsConfigurationSet(
        string apexARecord,
        string apexAliasRecord,
        string cApiCnameTarget,
        string fileCnameTarget)
    {
        ApexARecord = apexARecord;
        ApexAliasRecord = apexAliasRecord;
        CApiCnameTarget = cApiCnameTarget;
        FileCnameTarget = fileCnameTarget;

        AsciiDomainNameValidator.AssertValidDomain(ApexAliasRecord);
    }

    //
    
}