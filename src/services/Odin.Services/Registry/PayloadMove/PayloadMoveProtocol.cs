#nullable enable

namespace Odin.Services.Registry.PayloadMove;

// What the target and the source's payload move endpoint exchange

public class PayloadMoveRedeemRequest
{
    public string? HandoffToken { get; set; }
}

public class PayloadMoveRedeemResponse
{
    public string Credential { get; set; } = "";
}
