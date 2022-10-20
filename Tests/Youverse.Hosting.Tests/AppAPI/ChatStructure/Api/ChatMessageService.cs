using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Microsoft.VisualBasic;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

public class ChatMessageService
{
    private readonly ChatServerContext _serverContext;
    private readonly ChatCommandSender _commandSender;
    private readonly ConversationDefinitionService _conversationDefinitionService;
    private readonly ConversationService _conversationService;

    public ChatMessageService(ChatServerContext serverContext, ConversationDefinitionService conversationDefinitionService, ConversationService conversationService)
    {
        _serverContext = serverContext;
        _conversationDefinitionService = conversationDefinitionService;
        _conversationService = conversationService;
        _commandSender = new ChatCommandSender(serverContext);
    }

    public async Task SendMessage(ChatMessage message)
    {
        Guard.Argument(message, nameof(message)).NotNull()
            .Require(msg => msg.Id != Guid.Empty)
            .Require(msg=>msg.ConversationId != Guid.Empty);
        
        var convo = await _conversationDefinitionService.GetConversation(message.ConversationId);
        var recipients = convo.RecipientDotYouId;

        var fileMetadata = new UploadFileMetadata()
        {
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = DotYouSystemSerializer.Serialize(message),
                FileType = ChatMessage.FileType,
                GroupId = convo.Id,
                ClientUniqueId = message.Id
            },
            AccessControlList = AccessControlList.NewOwnerOnly
        };

        var instructionSet = UploadInstructionSet.WithRecipients(ChatApiConfig.Drive, recipients);
        instructionSet.TransitOptions.UseGlobalTransitId = true;

        await _serverContext.SendFile(fileMetadata, instructionSet);
    }

    public async Task<IEnumerable<RenderableChatMessage>> GetMessages(Guid convoId)
    {
        var msgs = this._conversationService.GetMessages(convoId);
        return msgs;
    }
    

    public async Task ReactToMessage(Guid groupId, Guid messageId, string reactionCode)
    {
        // var group = await _conversationDefinitionService.GetConversation(groupId);
        // var recipients = group.Members.Where(r => r != this._serverContext.Sender).ToList();
        //
        // await _commandSender.SendCommand(new SendReactionCommand()
        // {
        //     MessageId = messageId,
        //     GroupId = groupId,
        //     ReactionCode = reactionCode,
        //     Code = CommandCode.SendReaction,
        //     Recipients = recipients
        // });
    }
}