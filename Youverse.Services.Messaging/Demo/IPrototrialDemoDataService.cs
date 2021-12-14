using System.Threading.Tasks;

namespace Youverse.Services.Messaging.Demo
{
    /// <summary>
    /// Temporary service used to generate demo data for the prototrial 
    /// </summary>
    public interface IPrototrialDemoDataService
    {
        /// <summary>
        /// Add a set of contacts.  The list of contacts is fixed in a file
        /// </summary>
        public Task<bool> AddContacts();

        Task<bool> AddConnectionRequests();
        
        public Task SetProfiles();

    }
}