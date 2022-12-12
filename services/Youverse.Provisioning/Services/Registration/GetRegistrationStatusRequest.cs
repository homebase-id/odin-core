namespace Youverse.Provisioning.Services.Registration;

public class GetRegistrationStatusRequest
{
    public Guid FirstRunToken { get; set; }
    
    public string DomainName { get; set; }
}