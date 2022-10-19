using System;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;

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

    public ChatSynchronizer(ChatServerContext chatServerContext, ConversationDefinitionService conversationDefinitionService, ConversationService conversationService)
    {
        _chatServerContext = chatServerContext;
        _conversationDefinitionService = conversationDefinitionService;
        _conversationService = conversationService;
    }

    /// <summary>
    /// Retrieves latest data from the chat drive and processes sequentially; which is critical
    /// </summary>
    public void SynchronizeData()
    {
        //Get all chat files
        var queryParams = FileQueryParams.FromFileType(ChatApiConfig.Drive, ChatMessage.FileType, CommandBase.FileType);
        var qbr = _chatServerContext.QueryBatch(queryParams, _latestCursor).GetAwaiter().GetResult();
        _latestCursor = qbr.CursorState;

        //TODO: is QueryBatch returning the expected order, i.e. most recent first?

        var orderedResults = qbr.SearchResults.OrderBy(sr => sr.FileMetadata.Created).ToList();
        
        //route the commands
        foreach (var clientFileHeader in orderedResults)
        {
            //route the incoming file
            var appData = clientFileHeader.FileMetadata.AppData;

            //if command - process the command; and wait
            if (appData.FileType == CommandBase.FileType)
            {
                this.HandleCommandFile(clientFileHeader).GetAwaiter().GetResult();
                return;
            }

            //if chat message, put into list
            if (appData.FileType == ChatMessage.FileType)
            {
                this.HandleChatMessage(clientFileHeader).GetAwaiter().GetResult();
                return;
            }
        }
    }

    private async Task HandleChatMessage(ClientFileHeader clientFileHeader)
    {
        string currentUser = _chatServerContext.Sender;
        
        var appData = clientFileHeader.FileMetadata.AppData;

        var msg = DotYouSystemSerializer.Deserialize<ChatMessage>(appData.JsonContent);
        msg!.Sender = clientFileHeader.FileMetadata.SenderDotYouId;

        _conversationService.AddMessage(
            groupId: appData.GroupId,
            messageId: msg.Id,
            received: clientFileHeader.FileMetadata.Created,
            message: msg);
    }

    private async Task HandleChatReactionCommand(ClientFileHeader clientFileHeader)
    {
        var cmd = DotYouSystemSerializer.Deserialize<SendReactionCommand>(clientFileHeader.FileMetadata.AppData.JsonContent);
        var appData = clientFileHeader.FileMetadata.AppData;
        _conversationService.AddReaction(appData.GroupId, cmd!.MessageId, new Reaction()
        {
            Sender = clientFileHeader.FileMetadata.SenderDotYouId,
            ReactionValue = cmd!.ReactionCode
        });
    }

    private async Task HandleCommandFile(ClientFileHeader clientFileHeader)
    {
        //before running this command, check if it has been processed
        var code = (CommandCode)clientFileHeader.FileMetadata.AppData.DataType;

        var success = false;
        switch (code)
        {
            case CommandCode.CreateGroup:
                var cmd = DotYouSystemSerializer.Deserialize<CreateGroupCommand>(clientFileHeader.FileMetadata.AppData.JsonContent);
                await _conversationDefinitionService.JoinGroup(cmd!.ChatGroup);
                success = true;
                break;

            case CommandCode.RemoveFromGroup:
                // success = ProcessRemoveFromGroup(clientFileHeader);
                // _groupDefinitionService.RemoveMember(cmd.GroupId);
                break;

            case CommandCode.SendReaction:
                await HandleChatReactionCommand(clientFileHeader);
                success = true;
                break;

            default:
                throw new NotImplementedException("Unhandled command code");
        }

        if (success)
        {
            // delete the file so no other clients touch the file
            ExternalFileIdentifier fileId = new ExternalFileIdentifier()
            {
                TargetDrive = ChatApiConfig.Drive,
                FileId = clientFileHeader.FileId
            };

            _chatServerContext.DeleteFile(fileId);
        }
    }
}