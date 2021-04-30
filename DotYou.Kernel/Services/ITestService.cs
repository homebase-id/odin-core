using DotYou.Types;
using System;
using System.Threading.Tasks;

namespace DotYou.Kernel.Services.Verification
{


    public interface ITestService
    {

        Task Sender();
        Task Receiver(Dummy d);

    }

    public class TestService : ITestService
    {
        ISenderVerificationService _svc;
        IDotYouHttpClientProxy _httpProxy;

        public TestService(ISenderVerificationService svc, IDotYouHttpClientProxy httpProxy)
        {
            _svc = svc;
            _httpProxy = httpProxy;
        }
        public async Task Sender()
        {
            var dummy = new Dummy("pie", Guid.NewGuid());
            _svc.AddVerifiable(dummy, 100);

            await _httpProxy.Post((DotYouIdentity)"samwisegamgee.me", "/api/verify/mock", dummy);

        }

        public async Task Receiver(Dummy d)
        {
            await _svc.AssertTokenVerified((DotYouIdentity)"samwisegamgee.me", d);

            
        }
    }

    public class Dummy : IVerifiable
    {
        private string data;
        private Guid token;
        public Dummy(string d, Guid t)
        {
            this.data = d;
            this.token = t;
        }

        public string GetChecksum()
        {
            return Checksum.ComputeSHA512(data);
        }

        public Guid GetToken()
        {
            return Guid.NewGuid();
        }
    }
}
