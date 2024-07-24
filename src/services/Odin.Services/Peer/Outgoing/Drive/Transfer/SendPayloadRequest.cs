namespace Odin.Services.Peer.Outgoing.Drive.Transfer;

public class SendPayloadRequest
{
    public bool IsTempFile { get; set; }
    public PayloadTransferInstructionSet InstructionSet { get; set; }
}