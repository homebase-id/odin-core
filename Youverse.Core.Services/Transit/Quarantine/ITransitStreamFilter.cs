using System;
using System.IO;
using System.Threading.Tasks;
using Youverse.Core.Identity;
using Youverse.Core.Services.Storage;

namespace Youverse.Core.Services.Transit.Quarantine
{
    /// <summary>
    /// Holds contextual information needed by instances of <see cref="ITransitStreamFilter"/>
    /// </summary>
    public interface IFilterContext
    {
        DotYouIdentity Sender { get; set; }

        //TODO: what else is needed here?
    }
    
    public class FilterContext : IFilterContext
    {
        public DotYouIdentity Sender { get; set; }
    }

    /// <summary>
    /// Defines a filter capable of accepting, quarantining, or rejecting in coming payloads
    /// </summary>
    public interface ITransitStreamFilter
    {
        /// <summary>
        /// The identifier for the filter
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// Apply the filter to the incoming <param name="data"></param>
        /// </summary>
        /// <param name="context">Contextual information for this filter</param>
        /// <param name="part">The <see cref="FilePart"/> being processed by the filter</param>
        /// <param name="data">The stream of data to be processed</param>
        /// <returns>A <see cref="FilterResult"/> indicating the result of the filter</returns>
        Task<FilterResult> Apply(IFilterContext context, FilePart part, Stream data);
    }

    public class MustBeConnectedContactFilter : ITransitStreamFilter
    {
        public Guid Id => Guid.Parse("00000000-0000-0000-833e-8bf7bcf62478");

        public Task<FilterResult> Apply(IFilterContext context, FilePart part, Stream data)
        {
            var result = new FilterResult()
            {
                FilterId = this.Id,
                Recommendation = FilterAction.Accept
            };

            return Task.FromResult(result);
        }
    }
}