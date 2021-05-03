using DotYou.Types;
using System;
using System.Threading.Tasks;

namespace DotYou.Kernel
{
    /// <summary>
    /// Declares commands for sending HTTP requests to other DotYou Servers.
    /// </summary>
    public interface IDotYouHttpClientProxy: IDisposable
    {
        /// <summary>
        /// Posts <typeparamref name="T"/> to the <paramref name="dotYouId"/> server
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path"></param>
        /// <param name="payload"></param>
        /// <returns>True if the returned HttpStatusCode is successful, otherwise false.</returns>
        Task<bool> Post<T>(string path, T payload);
    }
}
