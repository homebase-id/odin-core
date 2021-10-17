using DotYou.Types;

namespace DotYou.Kernel.Services.Identity
{
    public interface IDomainCertificate
    {
        /// <summary>
        /// The identity held by the domain certificate
        /// </summary>
        /// <returns></returns>
        DotYouIdentity DotYouId { get; set; }
        
    }
}