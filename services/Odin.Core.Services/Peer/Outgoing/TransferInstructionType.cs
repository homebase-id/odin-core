namespace Odin.Core.Services.Peer.Outgoing;

/// <summary>
/// Specifies the type of instruction incoming from another identity 
/// </summary>
public enum TransferInstructionType
{
    None,
    DeleteLinkedFile,
    SaveFile
}