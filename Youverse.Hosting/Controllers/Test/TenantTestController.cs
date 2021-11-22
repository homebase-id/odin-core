using Microsoft.AspNetCore.Mvc;

namespace Youverse.Hosting.Controllers.Test
{
    [Route("/tenanttest")]
    public class TenantTestController : Controller
    {
        private readonly ITenantDependencyTest _td1;
        private readonly ITenantDependencyTest2 _td2;
        
        public TenantTestController(ITenantDependencyTest td1, ITenantDependencyTest2 td2)
        {
            _td1 = td1;
            _td2 = td2;
        }
        
        // GET
        public ActionResult<string> Index()
        {
            return _td1.Hello(Request.Host.ToString()) + "\n" + _td2.Hello(Request.Host.ToString());
        }
    }
}