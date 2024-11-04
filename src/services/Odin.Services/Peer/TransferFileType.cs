namespace Odin.Services.Peer;

public enum TransferFileType
{
    CommandMessage,
    Normal,
    EncryptedFileForFeed,
    EncryptedFileForFeedViaTransit,
    ReadReceipt
}