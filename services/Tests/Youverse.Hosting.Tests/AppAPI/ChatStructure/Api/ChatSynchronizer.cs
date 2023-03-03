using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps.CommandMessaging;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

/// <summary>
/// Retrieves latest chat messages and commands; distributes accordingly
/// </summary>
public class ChatSynchronizer
{
    private string _latestCursor;
    private readonly ChatServerContext _chatServerContext;
    private readonly ConversationDefinitionService _conversationDefinitionService;
    private readonly ConversationService _conversationService;
    private readonly ChatMessageFileService _chatMessageFileService;

    public ChatSynchronizer(ChatServerContext chatServerContext, ConversationDefinitionService conversationDefinitionService, ConversationService conversationService,
        ChatMessageFileService chatMessageFileService)
    {
        _chatServerContext = chatServerContext;
        _conversationDefinitionService = conversationDefinitionService;
        _conversationService = conversationService;
        _chatMessageFileService = chatMessageFileService;
    }

    /// <summary>
    /// Retrieves latest data from the chat drive and processes sequentially; which is critical
    /// </summary>
    public async Task SynchronizeData()
    {
        await this._chatServerContext.ProcessIncomingInstructions();

        await this.ProcessLatestCommands();

        // await this.ProcessIncomingChatMessages();
    }

    /// <summary>
    /// Get chat messages from the server add them to the various conversations
    /// </summary>
    private void ProcessIncomingChatMessages()
    {
        
    }

    /// <summary>
    /// Get the latest unprocessed commands handle accordingly; including
    /// updating associated files by global transit id
    /// </summary>
    private async Task ProcessLatestCommands()
    {
        var receivedCommandResultSet = await _chatServerContext.GetUnprocessedCommands();

        Dictionary<Guid, bool> commandCompletionStatus = new Dictionary<Guid, bool>();
        foreach (var receivedCommand in receivedCommandResultSet.ReceivedCommands)
        {
            var success = false;

            switch ((CommandCode)receivedCommand.ClientCode)
            {
                case CommandCode.JoinConversation:
                    var scc = DotYouSystemSerializer.Deserialize<JoinConversationCommand>(receivedCommand.ClientJsonMessage);
                    await _conversationDefinitionService.JoinConversation(scc.ConversationId, receivedCommand.Sender);
                    success = true;
                    break;

                case CommandCode.SendReaction:
                    await HandleChatReactionCommand(DotYouSystemSerializer.Deserialize<SendReactionCommand>(receivedCommand.ClientJsonMessage), receivedCommand.Sender);
                    success = true;
                    break;

                case CommandCode.SendReadReceipt:
                    await HandleReadReceiptCommand(DotYouSystemSerializer.Deserialize<SendReadReceiptCommand>(receivedCommand.ClientJsonMessage), receivedCommand.Sender);
                    success = true;
                    break;
            }

            commandCompletionStatus.Add(receivedCommand.Id, success);
        }

        var successCommands = commandCompletionStatus.Where(kvp => kvp.Value == true).Select(kvp => kvp.Key);
        await _chatServerContext.MarkCommandsCompleted(successCommands);

        //TODO: what to do w/ these?
        // var failedCommands =  commandCompletionStatus.Where(kvp => kvp.Value == false).Select(kvp => kvp.Key);
    }

    private async Task HandleReadReceiptCommand(SendReadReceiptCommand command, string sender)
    {
        var message = await _chatMessageFileService.GetChatMessageFile(command.ConversationId, command.MessageId);
        
        message.ReadReceipts.Add(new ReadReceipt()
        {
            Sender = sender,
            Timestamp = command.Timestamp
        });

        await _chatMessageFileService.UpdateMessage(message);
        
    }

    private async Task HandleChatReactionCommand(SendReactionCommand command, string sender)
    {
        var message = await _chatMessageFileService.GetChatMessageFile(command.ConversationId, command.MessageId);
        message.Reactions.Add(new Reaction()
        {
            Sender = sender,
            ReactionValue = command.ReactionCode,
            Timestamp = UnixTimeUtc.Now() //TODO: should this come from the sender?
        });

        await _chatMessageFileService.UpdateMessage(message);
        
        //
        
        // _conversationService.AddReaction(command.ConversationId, command.MessageId, new Reaction()
        // {
        //     Sender = sender,
        //     ReactionValue = command.ReactionCode
        // });

    }

    private async Task GetFilesForCommands(ReceivedCommandResultSet resultSet)
    {
        await Task.CompletedTask;
        throw new NotImplementedException("just saving a bit of code for short term");
        // foreach (var command in resultSet.ReceivedCommands)
        // {
        //     var fqp = new FileQueryParams()
        //     {
        //         TargetDrive = resultSet.TargetDrive,
        //         GlobalTransitId = command!.GlobalTransitIdList
        //     };
        //
        //     var options = new QueryBatchResultOptions()
        //     {
        //         Cursor = null, //?
        //         ExcludePreviewThumbnail = true,
        //         IncludeJsonContent = true,
        //         MaxRecords = int.MaxValue //??
        //     };
        //
        //     var globalTransitFileBatch = await _driveQueryService.GetBatch(driveId, fqp, options);
        //     
        //     
        //     var matchedFile = receivedCommand.MatchingFiles.SingleOrDefault();
        //     Assert.IsNotNull(matchedFile, "there should be only one matched file");
        //     Assert.IsTrue(matchedFile.FileId != originalFileSendResult.UploadedFile.FileId, "matched file should NOT have same Id as the one we uploaded since it was sent to a new identity");
        //     Assert.IsTrue(matchedFile.FileMetadata.GlobalTransitId == originalFileSendResult.GlobalTransitId, "The matched file should have the same global transit id as the file orignally sent");
        //     Assert.IsTrue(matchedFile.FileMetadata.AppData.JsonContent == originalFileSendResult.UploadFileMetadata.AppData.JsonContent,
        //         "matched file should have same JsonContent as the on we uploaded");
        //     Assert.IsTrue(matchedFile.FileMetadata.AppData.FileType == originalFileSendResult.UploadFileMetadata.AppData.FileType);
        //
        // }
    }
}