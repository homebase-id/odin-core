using System.Threading.Tasks;

namespace DotYou.Kernel.Services.Demo
{
    /// <summary>
    /// Temporary service used to generate demo data for the prototrial 
    /// </summary>
    public interface IPrototrialDemoDataService
    {
        /// <summary>
        /// Add a set of contacts.  The list of contacts is fixed in a file
        /// </summary>
        public Task<bool> AddDigitalIdentities();

        Task<bool> AddConnectionRequests();
        
        public Task SetProfiles();

    }
}