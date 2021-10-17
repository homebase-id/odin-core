using DotYou.Types;
using MagicOnion;

namespace DotYou.DigitalIdentityHost.IdentityRegistry
{
    //TODO: get this from a library instead
    
    /// <summary>
    /// Handles server-to-server communications for <see cref="IdentityRegistration"/> data
    /// </summary>
    public interface IRegistryRpcService : IService<IRegistryRpcService>
    {
        /// <summary>
        /// Gets a list of <see cref="IdentityRegistration"/> based on the paging options sorted by domain name ascending
        /// </summary>
        /// <param name="pageOptions"></param>
        /// <returns></returns>
        UnaryResult<PagedResult<IdentityRegistration>> GetList(PageOptions pageOptions);

        /// <summary>
        /// Gets an <see cref="IdentityRegistration"/> by domain name
        /// </summary>
        /// <param name="domainName"></param>
        /// <returns></returns>
        UnaryResult<IdentityRegistration> Get(string domainName);
    }
}