using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Odin.Services.Base
{
    public interface IOdinContextAccessor
    {
        OdinContext GetCurrent();

        void SetCurrent(OdinContext context);
    }

    /// <summary>
    /// Contains all information required to execute commands in the Odin.Services services.
    /// </summary>
    public class HttpOdinContextAccessor(IHttpContextAccessor accessor) : IOdinContextAccessor
    {
        private OdinContext _localContext;

        public OdinContext GetCurrent()
        {
            return _localContext ?? accessor.HttpContext.RequestServices.GetRequiredService<OdinContext>();
        }

        public void SetCurrent(OdinContext context)
        {
            _localContext = context;
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

        public void SetCurrent(OdinContext context)
        {
            throw new System.NotImplementedException();
        }
    }
}