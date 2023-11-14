using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Odin.Core.Services.Registry.Registration;
#nullable enable

public interface IAuthorativeDnsLookupResult
{
    string AuthorativeDomain { get; set; }
    string AuthorativeNameServer { get; set; } // NOTE: it is normal but not a requirement that this be in NameServers
    List<string> NameServers { get; set; }
    Exception? Exception { get; set; }
}

public interface IAuthorativeDnsLookup
{
    Task<IAuthorativeDnsLookupResult> LookupRootAuthority();

    /// <summary>
    /// Try to lookup nearest authorative data for a domain.
    /// Looking up the root (.) will return "".
    /// Note that this bypasses all caches and is slow. Do not use for performance sensitive queries.
    /// </summary>
    /// <param name="domain"></param>
    /// <returns>IAuthorativeDnsLookupResult</returns>
    Task<IAuthorativeDnsLookupResult> LookupDomainAuthority(string domain);

    /// <summary>
    /// Try to lookup zone apex for a domain (e.g. "www.example.com" => "example.com").
    /// Looking up the root (.) will return "".
    /// Note that this bypasses all caches and is slow. Do not use for performance sensitive queries.
    /// </summary>
    /// <param name="domain"></param>
    /// <returns>Zone apex on success, empty string if not found or error</returns>
    Task<string> LookupZoneApex(string domain);
}