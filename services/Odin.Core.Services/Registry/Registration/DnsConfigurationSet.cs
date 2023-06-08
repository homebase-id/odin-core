#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Odin.Core.Services.Registry.Registration;

public class DnsConfigurationSet
{
    public const string PrefixApi = "api";
    public const string PrefixCertApi = "capi";
    public const string PrefixFile = "file";
    public const string PrefixWww = "www";
    
    public static readonly string[] WellknownPrefixes = { PrefixApi, PrefixCertApi, PrefixFile, PrefixWww };

    public List<string> BareARecords { get; }
    public string WwwCnameTarget { get; }
    public string ApiCnameTarget { get; }
    public string CApiCnameTarget { get; }
    public string FileCnameTarget { get; }
    
    //
    
    public DnsConfigurationSet(
        IEnumerable<string> bareARecords,
        string wwwCnameTarget, 
        string apiCnameTarget, 
        string cApiCnameTarget, 
        string fileCnameTarget)
    {
        WwwCnameTarget = wwwCnameTarget;
        ApiCnameTarget = apiCnameTarget;
        CApiCnameTarget = cApiCnameTarget;
        FileCnameTarget = fileCnameTarget;
        BareARecords = bareARecords.ToList();
    }
    
    //
    
}