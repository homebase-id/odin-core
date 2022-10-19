using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

public class ChatMessageService
{
    private readonly ChatServerContext _serverContext;
    private readonly ChatCommandSender _commandSender;
    private readonly ConversationDefinitionService _groupDefinitionService;
    private readonly ConversationService _conversationService;

    public ChatMessageService(ChatServerContext serverContext, ConversationDefinitionService groupDefinitionService, ConversationService conversationService)
    {
        _serverContext = serverContext;
        _groupDefinitionService = groupDefinitionService;
        _conversationService = conversationService;
        _commandSender = new ChatCommandSender(serverContext);
    }

    public async Task SendMessage(ChatMessage message)
    {
        var group = await _groupDefinitionService.GetGroup(message.GroupId);
        var recipients = group.Members.Where(r => r != this._serverContext.Sender).ToList();

        var fileMetadata = new UploadFileMetadata()
        {
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = DotYouSystemSerializer.Serialize(message),
                FileType = ChatMessage.FileType,
                GroupId = group.Id,
                //ClientUniqueId = message.Id
            },
            AccessControlList = AccessControlList.NewOwnerOnly
        };

        var instructionSet = UploadInstructionSet.WithRecipients(ChatApiConfig.Drive, recipients);
        await _serverContext.SendFile(fileMetadata, instructionSet);
    }

    public async Task<IEnumerable<RenderableChatMessage>> GetMessages(Guid groupId)
    {
        var msgs = this._conversationService.GetMessages(groupId);
        return msgs;
    }


    public IEnumerable<ChatGroup> GetGroups()
    {
        //query my server for all chat group file types
        var prevCursorState = "";
        var query = new FileQueryParams() { FileType = new List<int>() { ChatGroup.GroupDefinitionFileType } };
        var (groups, cursorState) = this._serverContext.QueryBatch<ChatGroup>(query, prevCursorState).GetAwaiter().GetResult();

        return groups;
    }

    public async Task React(Guid groupId, Guid messageId, string reactionCode)
    {
        var group = await _groupDefinitionService.GetGroup(groupId);
        var recipients = group.Members.Where(r => r != this._serverContext.Sender).ToList();

        await _commandSender.SendCommand(new SendReactionCommand()
        {
            MessageId = messageId,
            GroupId = groupId,
            ReactionCode = reactionCode,
            Code = CommandCode.SendReaction,
            Recipients = recipients
        });
    }
}