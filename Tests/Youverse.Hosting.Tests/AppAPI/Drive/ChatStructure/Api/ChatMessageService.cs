using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class ChatMessageService
{
    private readonly ChatServerContext _ctx;
    private readonly ChatGroupDefinitionService _groupDefinitionService;

    public ChatMessageService(ChatServerContext ctx, ChatGroupDefinitionService groupDefinitionService)
    {
        _ctx = ctx;
        _groupDefinitionService = groupDefinitionService;
    }

    // public async Task SendChatMessage(TestIdentity sender, TestIdentity recipient, string message)
    // {
    //     var groupId = ByteArrayUtil.EquiByteArrayXor(sender.DotYouId.ToGuidIdentifier().ToByteArray(), recipient.DotYouId.ToGuidIdentifier().ToByteArray());
    //     await SendGroupMessage(
    //         new Guid(groupId),
    //         message: new ChatMessage() { Message = message },
    //         recipients: new List<string>() { recipient.DotYouId });
    // }

    public async Task SendMessage(Guid groupId, ChatMessage message)
    {
        //TODO: save a copy on the sender's server too.
        var m = new ChatGroupMessage()
        {
            GroupId = groupId,
            ChatMessage = message
        };
        
        var group = await _groupDefinitionService.GetGroup(groupId);
        
        var recipients = recipients.Where(r => r != this._ctx.Sender).ToList();

        var fileMetadata = new UploadFileMetadata()
        {
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = false,
                JsonContent = DotYouSystemSerializer.Serialize(message),
                FileType = ChatGroupMessage.FileType,
                GroupId = groupId
            },
            AccessControlList = new AccessControlList()
            {
                RequiredSecurityGroup = SecurityGroupType.Owner
            }
        };

        var instructionSet = new UploadInstructionSet()
        {
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            StorageOptions = new StorageOptions()
            {
                Drive = ChatApiConfig.Drive,
                OverwriteFileId = null,
                ExpiresTimestamp = null
            },
            TransitOptions = new TransitOptions()
            {
                Recipients = recipients
            }
        };

        await _ctx.SendFile(fileMetadata, instructionSet);
    }

    public async Task<(IEnumerable<ChatMessage> messages, string CursorState)> GetMessages(string cursorState)
    {
        var queryParams = new FileQueryParams()
        {
            FileType = new List<int>() { ChatMessage.FileType }
        };

        var (messages, cursor) = await _ctx.QueryBatch<ChatMessage>(queryParams, cursorState);

        return (messages, cursor);
    }

    public async Task<(IEnumerable<ChatGroupMessage> messages, string CursorState)> GetGroupMessages(Guid groupId, string cursorState)
    {
        var queryParams = new FileQueryParams()
        {
            FileType = new List<int>() { ChatMessage.FileType },
            GroupId = new List<Guid>() { groupId }
        };

        var batch = await _ctx.QueryBatch(queryParams, cursorState);

        var messages = batch.SearchResults.Select(item => new ChatGroupMessage()
        {
            GroupId = item.FileMetadata.AppData.GroupId,
            ChatMessage = DotYouSystemSerializer.Deserialize<ChatMessage>(item.FileMetadata.AppData.JsonContent)
        });

        return (messages, batch.CursorState);
    }


    public IEnumerable<ChatGroup> GetGroups()
    {
        //query my server for all chat group file types
        var prevCursorState = "";
        var query = new FileQueryParams() { FileType = new List<int>() { ChatGroup.GroupDefinitionFileType } };
        var (groups, cursorState) = this._ctx.QueryBatch<ChatGroup>(query, prevCursorState).GetAwaiter().GetResult();

        return groups;
    }
}