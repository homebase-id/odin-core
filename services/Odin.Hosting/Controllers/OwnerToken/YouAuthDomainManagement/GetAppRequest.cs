namespace Odin.Hosting.Controllers.OwnerToken.YouAuthDomainManagement;

public class GetYouAuthDomainRequest
{
    public string Domain { get; set; }
}

public class GetYouAuthDomainClientRequest
{
    public GuidId AccessRegistrationId { get; set; }
}