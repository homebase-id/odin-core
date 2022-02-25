using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Youverse.Core.Services.Base
{
    /// <summary>
    /// Contains all information required to execute commands in the Youverse.Core.Services services.
    /// </summary>
    public class DotYouContextAccessor
    {
        private readonly IHttpContextAccessor _accessor;

        public DotYouContextAccessor(IHttpContextAccessor accessor)
        {
            _accessor = accessor;
        }

        public DotYouContext GetCurrent()
        {
            return _accessor.HttpContext.RequestServices.GetRequiredService<DotYouContext>();
            // return _accessor.HttpContext.RequestServices.GetAutofacRoot().Resolve<DotYouContext>();
        }
    }
}