using System.Threading.Tasks;

namespace Odin.Core.Services.Registry.Registration;
#nullable enable

public interface IAuthorativeDnsLookup
{
    Task<string?> Lookup(string domain);
}