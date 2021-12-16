using System;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Drive.Security
{
    public interface IGranteeResolver
    {
        /// <summary>
        /// Resolves the <see cref="GranteeIdentity"/>
        /// </summary>
        /// <param name="domainIdentity">The <see cref="GranteeIdentity.DomainIdentity"/></param>
        /// <returns></returns>
        Task<GranteeIdentity> Resolve(string domainIdentity);

        /// <summary>
        /// Resolve the <see cref="GranteeIdentity"/>
        /// </summary>
        /// <param name="id">The <see cref="GranteeIdentity.Id"/> </param>
        /// <returns></returns>
        Task<GranteeIdentity> Resolve(Guid id);

    }
}