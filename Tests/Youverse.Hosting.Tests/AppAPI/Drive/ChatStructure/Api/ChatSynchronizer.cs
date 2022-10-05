using System;
using System.Threading.Tasks;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

/// <summary>
/// Retrieves latest chat messages and commands; distributes accordingly
/// </summary>
public class ChatSynchronizer
{
    private string _latestCursor;
    private readonly ChatServerContext _chatServerContext;
    private readonly ChatGroupDefinitionService _groupDefinitionService;
    
    public ChatSynchronizer(ChatServerContext chatServerContext, ChatGroupDefinitionService groupDefinitionService)
    {
        _chatServerContext = chatServerContext;
        _groupDefinitionService = groupDefinitionService;
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
        
        //route the commands
        foreach (var clientFileHeader in qbr.SearchResults)
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
                //clientFileHeader.FileMetadata.SenderDotYouId
                var msg = DotYouSystemSerializer.Deserialize<ChatMessage>(appData.JsonContent);
                //HandleMessage(msg);
                return;
            }

            if (appData.FileType == ChatGroupMessage.FileType)
            {
                var msg = DotYouSystemSerializer.Deserialize<ChatGroupMessage>(appData.JsonContent);
                //HandleGroupMessage(msg);
                return;
            }
        }
    }

    // private void HandleGroupMessage(ChatGroupMessage msg)
    // {
    //     _chatData.GroupMessages.Add(msg.GroupId, msg);
    // }
    //
    // private void HandleMessage(ChatMessage msg)
    // {
    //     _chatData.Messages.Add(msg);
    // }
    
    
    public async Task HandleCommandFile(ClientFileHeader clientFileHeader)
    {
        //before running this command, check if it has been processed

        var code = (CommandCode)clientFileHeader.FileMetadata.AppData.DataType;

        var success = false;
        switch (code)
        {
            case CommandCode.CreateGroup:
                var cmd = DotYouSystemSerializer.Deserialize<CreateGroupCommand>(clientFileHeader.FileMetadata.AppData.JsonContent);
                await _groupDefinitionService.JoinGroup(cmd!.ChatGroup);
                success = true;
                break;

            case CommandCode.RemoveFromGroup:
                // success = ProcessRemoveFromGroup(clientFileHeader);
                // _groupDefinitionService.RemoveMember(cmd.GroupId);
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