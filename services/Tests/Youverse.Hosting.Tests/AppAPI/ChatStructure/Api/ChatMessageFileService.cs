using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drives.DriveCore.Query;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

public class ChatMessageFileService
{
    private readonly ChatServerContext _serverContext;
    private readonly ChatCommandSender _commandSender;
    private readonly ConversationDefinitionService _conversationDefinitionService;
    private readonly ConversationService _conversationService;

    public ChatMessageFileService(ChatServerContext serverContext, ConversationDefinitionService conversationDefinitionService, ConversationService conversationService)
    {
        _serverContext = serverContext;
        _conversationDefinitionService = conversationDefinitionService;
        _conversationService = conversationService;
        _commandSender = new ChatCommandSender(serverContext);
    }

    public async Task SendMessage(Guid id, Guid conversationId, string messageText)
    {
        Guard.Argument(id, nameof(id)).Require(v => v != Guid.Empty);
        Guard.Argument(conversationId, nameof(conversationId)).Require(v => v != Guid.Empty);
        Guard.Argument(messageText, nameof(messageText)).Require(v => !string.IsNullOrEmpty(v) && !string.IsNullOrWhiteSpace(v));

        var convo = await _conversationDefinitionService.GetConversation(conversationId);

        var message = new ChatMessage()
        {
            Id = id,
            ConversationId = conversationId,
            Text = messageText,
            Reactions = new List<Reaction>(),
            ReadReceipts = new List<ReadReceipt>()
        };

        await this.UploadFile(message, convo.RecipientOdinId);
    }

    public async Task UpdateMessage(ChatMessage message)
    {
        Guard.Argument(message, nameof(message)).NotNull()
            .Require(msg => msg.Id != Guid.Empty)
            .Require(msg => msg.ConversationId != Guid.Empty);

        await this.UploadFile(message);
    }

    public async Task<IEnumerable<ChatMessage>> GetMessages(Guid convoId, string cursor = "")
    {
        var queryParams = FileQueryParams.FromFileType(ChatApiConfig.Drive, ChatMessage.FileType);
        queryParams.GroupId = new List<Guid>() { convoId };
        var qbr = _serverContext.QueryBatch(queryParams, cursor).GetAwaiter().GetResult();
        cursor = qbr.CursorState;

        // throw new Exception("need to process newest to oldest - due to issues where we have 1000000 messages");
        //HACK: order oldest to newest until Michael adds core support for this
        var orderedResults = qbr.SearchResults.OrderBy(sr => sr.FileMetadata.Created).ToList();

        var results = new List<ChatMessage>();

        foreach (var clientFileHeader in orderedResults)
        {
            results.Add(File2ChatMessage(clientFileHeader));

            //sync it locally
            // _conversationService.AddMessage(
            //     convoId: appData.GroupId,
            //     messageId: msg.Id,
            //     received: clientFileHeader.FileMetadata.Created,
            //     message: msg);
        }

        return await Task.FromResult(results);
    }

    public async Task ReactToMessage(Guid conversationId, Guid messageId, string reactionCode)
    {
        var conversation = await _conversationDefinitionService.GetConversation(conversationId);

        await _commandSender.SendCommand(new SendReactionCommand()
        {
            MessageId = messageId,
            ConversationId = conversationId,
            ReactionCode = reactionCode,
            Recipients = new List<string>() { conversation.RecipientOdinId }
        });
    }

    public async Task NotifyMessageWasRead(Guid conversationId, Guid messageId)
    {
        var conversation = await _conversationDefinitionService.GetConversation(conversationId);

        await _commandSender.SendCommand(new SendReadReceiptCommand()
        {
            MessageId = messageId,
            ConversationId = conversationId,
            Recipients = new List<string>() { conversation.RecipientOdinId },
            Timestamp = UnixTimeUtc.Now()
        });
    }
    public async Task<ChatMessage> GetChatMessageFile(Guid conversationId, Guid messageId)
    {
        var file = await this.GetChatMessageFileById(conversationId, messageId);
        return File2ChatMessage(file);
    }

    private async Task<SharedSecretEncryptedFileHeader> GetChatMessageFileById(Guid conversationId, Guid messageId)
    {
        
        var queryParams = new FileQueryParams()
        {
            TargetDrive = ChatApiConfig.Drive,
            TagsMatchAll = new List<Guid>() { messageId }, //HACK: Use a tag until Michael finishes indexing client unique Id
            // ClientUniqueId = messageId
            GroupId = new List<Guid>() { conversationId }
        };

        var queryBatchResponse = await this._serverContext.QueryBatch(queryParams, "");

        if (queryBatchResponse.SearchResults.Count() > 1)
        {
            throw new Exception($"too many chat files with id {messageId} returned");
        }

        var file = queryBatchResponse.SearchResults.SingleOrDefault();
        return file;
    }

    private ChatMessage File2ChatMessage(SharedSecretEncryptedFileHeader sharedSecretEncryptedFileHeader)
    {
        var appData = sharedSecretEncryptedFileHeader.FileMetadata.AppData;
        var message = DotYouSystemSerializer.Deserialize<ChatMessage>(appData.JsonContent);

        //TODO: add checks for file corruption - 
        // if appData.GroupId != message.ConversationId
        // if message is null or did not deserialize correctly

        message!.Sender = string.IsNullOrEmpty(sharedSecretEncryptedFileHeader.FileMetadata.SenderOdinId) ? _serverContext.Sender : sharedSecretEncryptedFileHeader.FileMetadata.SenderOdinId;
        message!.ReceivedTimestamp = sharedSecretEncryptedFileHeader.FileMetadata.Created; //TODO: determine if this makes the most sense
        return message;
    }

    private async Task UploadFile(ChatMessage message, params string[] recipients)
    {
        var existingFile = await this.GetChatMessageFileById(message.ConversationId, message.Id);

        //Note: the JsonContent holds the actual message and is the source of truth
        // we use attributes from the message to index it to make it easier to find
        // when you deserialize the message, you should do so from JsonContent
        var fileMetadata = new UploadFileMetadata()
        {
            ContentType = "application/json",
            AllowDistribution = true,
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = DotYouSystemSerializer.Serialize(message),
                FileType = ChatMessage.FileType,
                GroupId = message.ConversationId,
                // UniqueId = message.Id,
                Tags = new List<Guid>() { message.Id } //HACK: Until michael adds search for ClientUniqueId
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        var instructionSet = UploadInstructionSet.WithTargetDrive(ChatApiConfig.Drive);
        instructionSet.TransitOptions.UseGlobalTransitId = existingFile != null;
        instructionSet.StorageOptions.OverwriteFileId = existingFile?.FileId;
        instructionSet.TransitOptions.Recipients = recipients?.ToList();

        await _serverContext.SendFile(fileMetadata, instructionSet);
    }
}