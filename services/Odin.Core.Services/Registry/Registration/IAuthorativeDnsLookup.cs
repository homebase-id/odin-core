using System.Threading.Tasks;

namespace Odin.Core.Services.Registry.Registration;
#nullable enable

public interface IAuthorativeDnsLookup
{
    /// <summary>
    /// Try to lookup authorative nameserver for a domain.
    /// Looking up the root (.) will return "".
    /// Note that this bypasses all caches and is slow. Do not use for performance sensitive queries.
    /// </summary>
    /// <param name="domain"></param>
    /// <returns>Nameserver on success, empty string if not found or error</returns>
    Task<string> LookupNameServer(string domain);

    /// <summary>
    /// Try to lookup zone apex for a domain (e.g. "www.example.com" => "example.com").
    /// Looking up the root (.) will return "".
    /// Note that this bypasses all caches and is slow. Do not use for performance sensitive queries.
    /// </summary>
    /// <param name="domain"></param>
    /// <returns>Zone apex on success, empty string if not found or error</returns>
    Task<string> LookupZoneApex(string domain);
}