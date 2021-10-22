using System.Threading.Tasks;

namespace Youverse.Core.Services.Transit
{
    /// <summary>
    /// Handles incoming payloads
    /// </summary>
    public interface ITransitReceiverService
    {
        /// <summary>
        /// Filters, Triages, and distributes the incoming payload the right handler
        /// </summary>
        /// <returns></returns>
        Task HandleIncomingPayload();
    }

    public class TransitReceiverService : ITransitReceiverService
    {
        public Task HandleIncomingPayload()
        {
            //filter, triage, decrypt, route the payload
            return Task.CompletedTask;
        }
    }
}