using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Dns;
#nullable enable

public interface IAuthoritativeDnsLookupResult
{
    string AuthoritativeDomain { get; set; }
    string AuthoritativeNameServer { get; set; } // NOTE: it is normal but not a requirement that this be in NameServers
    List<string> NameServers { get; set; }
    Exception? Exception { get; set; }
}

public interface IAuthoritativeDnsLookup
{
    Task<IAuthoritativeDnsLookupResult> LookupRootAuthority();

    /// <summary>
    /// Try to lookup nearest authoritative data for a domain.
    /// Looking up the root (.) will return "".
    /// Note that this bypasses all caches and is slow. Do not use for performance sensitive queries.
    /// </summary>
    /// <param name="domain"></param>
    /// <returns>IAuthoritativeDnsLookupResult</returns>
    Task<IAuthoritativeDnsLookupResult> LookupDomainAuthority(string domain);

    /// <summary>
    /// Try to lookup zone apex for a domain (e.g. "www.example.com" => "example.com").
    /// Looking up the root (.) will return "".
    /// Note that this bypasses all caches and is slow. Do not use for performance sensitive queries.
    /// </summary>
    /// <param name="domain"></param>
    /// <returns>Zone apex on success, empty string if not found or error</returns>
    Task<string> LookupZoneApex(string domain);
}