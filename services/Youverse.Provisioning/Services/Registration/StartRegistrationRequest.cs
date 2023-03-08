namespace Youverse.Provisioning.Services.Registration;

public class StartRegistrationRequest
{
    public string DomainName { get; set; } = "";

    public RegistrationInfo RegistrationInfo { get; set; } = new();
}