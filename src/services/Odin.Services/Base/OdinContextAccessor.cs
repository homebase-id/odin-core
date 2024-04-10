using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Odin.Services.Base
{
    public interface IOdinContextAccessor
    {
        OdinContext GetCurrent();
    }

    /// <summary>
    /// Contains all information required to execute commands in the Odin.Services services.
    /// </summary>
    public class HttpOdinContextAccessor(IHttpContextAccessor accessor) : IOdinContextAccessor
    {
        public OdinContext GetCurrent()
        {
            return accessor.HttpContext.RequestServices.GetRequiredService<OdinContext>();
        }
    }

    /// <summary>
    /// Context accessor when you want to set an explicit OdinContext
    /// </summary>
    /// <param name="context"></param>
    public class ExplicitOdinContextAccessor(OdinContext context) : IOdinContextAccessor
    {
        public OdinContext GetCurrent()
        {
            return context;
        }
    }
}