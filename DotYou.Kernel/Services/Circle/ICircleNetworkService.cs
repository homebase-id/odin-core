using DotYou.Types;
using System.Threading.Tasks;

namespace DotYou.Kernel.Services.Circle
{
    /// <summary>
    /// Establishes connections between individuals
    /// </summary>
    public interface ICircleNetworkService
    {
        /// <summary>
        /// Gets the <see cref="SystemCircle"/> in which the specified <param name="dotYouId"></param> belongs.
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<SystemCircle> GetSystemCircle(DotYouIdentity dotYouId);

        /// <summary>
        /// Disconnects you from the specified <see cref="DotYouIdentity"/>
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<bool> Disconnect(DotYouIdentity dotYouId);

        /// <summary>
        /// Blocks the specified <see cref="DotYouIdentity"/> from your network
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<bool> Block(DotYouIdentity dotYouId);

        /// <summary>
        /// Unblocks the specified <see cref="DotYouIdentity"/> from your network
        /// </summary>
        /// <param name="dotYouId"></param>
        /// <returns></returns>
        Task<bool> Unblock(DotYouIdentity dotYouId);
    }
}