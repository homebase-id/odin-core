using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Apps;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Query;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.AppAPI.Drive.ChatStructure.Api;

public class ChatCommandService
{
    private readonly ChatContext _context;

    public ChatCommandService(ChatContext context)
    {
        _context = context;
    }

    public Guid CreateGroup(string title, List<string> members)
    {
        //create a command file
        //send it

        var cmd = new CreateGroupCommand()
        {
            GroupId = Guid.NewGuid(),
            Title = title,
            Recipients = members,
            Code = CommandCode.CreateGroup
        };

        this.SendCommand(cmd).GetAwaiter().GetResult();
        return cmd.GroupId;
    }
    
    private async Task SendCommand(CommandBase cmd)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = DotYouSystemSerializer.Serialize(cmd, cmd.GetType()),
                FileType = CommandBase.FileType,
                DataType = (int)cmd.Code
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
                Recipients = cmd.Recipients.Where(r => r != _context.Sender).ToList()
            }
        };

        await _context.SendFile(fileMetadata, instructionSet);
    }

    private void ProcessCreateGroup(CreateGroupCommand cmd)
    {
        //create a group file and upload to my server

        //query server to ensure the group has not already been created
        var chatGroup = new ChatGroup()
        {
            Id = cmd.GroupId,
            Title = cmd.Title,
            Members = cmd.Recipients
        };

        var fileMetadata = new UploadFileMetadata()
        {
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = DotYouSystemSerializer.Serialize(chatGroup),
                FileType = ChatGroup.GroupFileType
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
        };
        
        _context.SendFile(fileMetadata, instructionSet).GetAwaiter().GetResult();
    }

    public void HandleCommandFile(ClientFileHeader clientFileHeader)
    {
        
        //before running this command, check if it has been processed
        
        // foreach (var cmd in commands)
        // {
        //     switch (cmd.Code)
        //     {
        //         case CommandCode.CreateGroup:
        //             ProcessCreateGroup((CreateGroupCommand)cmd);
        //             break;
        //
        //         case CommandCode.RemoveFromGroup:
        //             throw new NotImplementedException("TODO");
        //
        //         default:
        //             throw new ArgumentOutOfRangeException();
        //     }
        // }
        
        
        // delete the file so no other clients touch the file
        ExternalFileIdentifier fileId = new ExternalFileIdentifier()
        {
            TargetDrive = ChatApiConfig.Drive,
            FileId = clientFileHeader.FileMetadata.File.FileId
        };
        
        _context.DeleteFile(fileId);
    }
}