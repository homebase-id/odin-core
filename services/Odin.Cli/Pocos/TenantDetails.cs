using Odin.Core.Services.Registry;

namespace Odin.Cli.Pocos;

public class TenantDetails
{
    public IdentityRegistration Registration { get; set; }
    public string RegistrationPath = "";
    public long RegistrationSize = 0;
    public List<Payload> Payloads = new ();
    public long PayloadSize => Payloads.Sum(p => p.Size);

    public TenantDetails(IdentityRegistration registration)
    {
        Registration = registration;
    }

    public class Payload
    {
        public string Shard { get; set; } = "";
        public long Size { get; set; } = 0;
        public string Path { get; set; } = "";
    }
}