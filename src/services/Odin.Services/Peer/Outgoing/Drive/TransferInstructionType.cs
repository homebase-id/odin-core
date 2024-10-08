namespace Odin.Services.Peer.Outgoing.Drive;

/// <summary>
/// Specifies the type of instruction incoming from another identity 
/// </summary>
public enum TransferInstructionType
{
    None,
    DeleteLinkedFile,
    SaveFile,
    UpdateFile,
    ReadReceipt,
    AddReaction,
    DeleteReaction
}