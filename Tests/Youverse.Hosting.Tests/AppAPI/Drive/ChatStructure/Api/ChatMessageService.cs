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
    private const int ChatFileType = 101;

    private readonly ChatContext _ctx;

    public ChatMessageService(ChatContext ctx)
    {
        _ctx = ctx;
    }

    public async Task SendChatMessage(TestIdentity sender, TestIdentity recipient, string message)
    {
        var groupId = ByteArrayUtil.EquiByteArrayXor(sender.DotYouId.ToGuidIdentifier().ToByteArray(), recipient.DotYouId.ToGuidIdentifier().ToByteArray());
        await SendGroupMessage(
            sender,
            new Guid(groupId),
            message: new ChatMessage() { Message = message },
            recipients: new List<string>() { recipient.DotYouId });
    }

    public async Task SendGroupMessage(TestIdentity sender, Guid groupId, ChatMessage message, List<string> recipients)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = false,
                JsonContent = DotYouSystemSerializer.Serialize(message),
                FileType = ChatFileType,
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
                Recipients = recipients.Where(r => r != this._ctx.Sender).ToList()
            }
        };

        await _ctx.SendFile(fileMetadata, instructionSet);
    }

    public async Task<(IEnumerable<ChatMessage> messages, string CursorState)> GetMessages(TestIdentity identity, string cursorState)
    {
        var queryParams = new FileQueryParams()
        {
            FileType = new List<int>() { ChatFileType }
        };

        var (messages, cursor) = await _ctx.QueryBatch<ChatMessage>(identity, queryParams, cursorState);

        return (messages, cursor);
    }
}