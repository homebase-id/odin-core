using System.Threading.Tasks;

namespace Odin.Core.Services.Registry.Registration;
#nullable enable

public interface IAuthorativeDnsLookup
{
    /// <summary>
    /// Try to lookup authorative nameserver for a domain. Looking up the root (.) will return "".
    /// </summary>
    /// <param name="domain"></param>
    /// <returns>Nameserver on success, empty string if not found or error</returns>
    Task<string> Lookup(string domain);
}