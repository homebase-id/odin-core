using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps.CommandMessaging;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Transit.Upload;
using Youverse.Hosting.Controllers.ClientToken.App;
using Youverse.Hosting.Tests.AppAPI.CommandSender;

namespace Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;

public class ChatCommandSender
{
    private readonly ChatServerContext _serverContext;

    public ChatCommandSender(ChatServerContext serverContext)
    {
        _serverContext = serverContext;
    }

    // 

    public async Task SendCommand(CommandBase cmd)
    {
        // var command = new CommandMessage()
        // {
        //     Drive = ChatApiConfig.Drive,
        //     JsonMessage = DotYouSystemSerializer.Serialize(new { reaction = ":)" }),
        //     GlobalTransitIdList = new List<Guid>() { originalFileSendResult.GlobalTransitId.GetValueOrDefault() },
        //     Recipients = cmd.Recipients
        // };

        //
        // using (var client = _serverContext.Scaffold.AppApi.CreateAppApiHttpClient(senderTestContext.Identity, senderTestContext.ClientAuthenticationToken))
        // {
        //     var cmdService = RefitCreator.RestServiceFor<IAppCommandSenderHttpClient>(client, senderTestContext.SharedSecret);
        //     var sendCommandResponse = await cmdService.SendCommand(new SendCommandRequest()
        //     {
        //         Command = command
        //     });
        //
        //     Assert.That(sendCommandResponse.IsSuccessStatusCode, Is.True);
        //     Assert.That(sendCommandResponse.Content, Is.Not.Null);
        //     var commandResult = sendCommandResponse.Content;
        //
        //
        //     //TODO: add checks that the command was sent
        //     // Assert.That(commandResult.File, Is.Not.Null);
        //     // Assert.That(commandResult.File.FileId, Is.Not.EqualTo(Guid.Empty));
        //     // Assert.IsTrue(commandResult.File.TargetDrive.IsValid());
        //
        //     Assert.That(commandResult.RecipientStatus, Is.Not.Null);
        //     Assert.IsTrue(commandResult.RecipientStatus.Count == 3);
        //
        //     await _scaffold.OwnerApi.ProcessOutbox(senderTestContext.Identity, batchSize: commandResult.RecipientStatus.Count + 100);
        // }
        var fileMetadata = new UploadFileMetadata()
        {
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = DotYouSystemSerializer.Serialize(cmd, cmd.GetType()),
                FileType = CommandBase.FileType,
                DataType = (int)cmd.Code,
                GroupId = cmd.GroupId
            },
            AccessControlList = AccessControlList.NewOwnerOnly
        };

        var instructionSet = UploadInstructionSet.WithRecipients(ChatApiConfig.Drive, cmd.Recipients.Where(r => r != _serverContext.Sender));
        var result = await _serverContext.SendFile(fileMetadata, instructionSet);

        //TODO: consider how to handle when not all recipients received the transfer
        //examine: result.RecipientStatus[]
    }
}