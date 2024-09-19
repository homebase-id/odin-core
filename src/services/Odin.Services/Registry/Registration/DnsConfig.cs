using System.Collections.Generic;

namespace Odin.Services.Registry.Registration;

// NOTE: Frontend depends on this class layout, so be careful when changing it
public class DnsConfig
{
    public string Type { get; init; } = ""; // e.g. "CNAME"
    public string Name { get; init; } = ""; // e.g. "file" or ""
    public string Domain { get; init; } = ""; // e.g. "file.example.com" or "example.com"
    public string Value { get; init; } = ""; // e.g. "example.com" or "127.0.0.1"
    public string AltValue { get; init; } = ""; // For backwards compatibility using CNAME => CNAME => A
    public string Description { get; init; } = "";
    public DnsLookupRecordStatus Status { get; set; } = DnsLookupRecordStatus.Unknown;

    public Dictionary<string, DnsLookupRecordStatus> QueryResults { get; } = new (); // query results per DNS ip address
    public Dictionary<string, string[]> Records { get; } = new (); // parsed records per DNS ip address
}
