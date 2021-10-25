using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Youverse.Core.Identity;

namespace Youverse.Core.Services.Profile
{
    /// <summary>
    /// Services for managing profiles about with humans with which I'm connected.
    /// </summary>
    public interface IProfileService
    {
        /// <summary>
        /// Retrieves a profile by their <see cref="DotYouIdentity"/>.  This will pull a local
        /// cached copy if it exists, otherwise it will retrieve from the <see cref="DotYouIdentity"/> DI
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<DotYouProfile> Get(DotYouIdentity dotYouId);

        /// <summary>
        /// Gets the public key used for encrypting the key header when transmitting data.
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <param name="forceRefresh">If true, the <param name="dotYouId">dotYouId</param> server will be queried to get the latest Key Header public key</param>
        /// <returns>The public key if available, otherwise null</returns>
        Task<byte[]> GetPublicKeyForKeyHeader(DotYouIdentity dotYouId, bool forceRefresh = false);

        /// <summary>
        /// Upserts a profile into the system.
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        Task Save(DotYouProfile profile);

        /// <summary>
        /// Deletes the specified profile
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task Delete(DotYouIdentity dotYouId);

        /// <summary>
        /// Finds contacts matching the given predicate.
        /// </summary>
        Task<PagedResult<DotYouProfile>> Find(Expression<Func<DotYouProfile, bool>> predicate, PageOptions req);
    }
}