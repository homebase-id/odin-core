namespace Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth;

public class GetYouAuthDomainClientRequest
{
    public GuidId AccessRegistrationId { get; set; }
}

public class GrantYouAuthDomainCircleRequest
{
    public string Domain { get; set; }
    public GuidId CircleId { get; set; }
}

public class RevokeYouAuthDomainCircleRequest
{
    public string Domain { get; set; }
    public GuidId CircleId { get; set; }
}