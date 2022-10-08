using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Core.Services.Apps.CommandMessaging;

/// <summary>
/// Enables apps to send commands to other identities for notifications
/// </summary>
/// <remarks>
/// Uses transit to send commands as special files
/// </remarks>
public class CommandMessagingService
{
    private readonly ITransitService _transitService;
    private readonly IDriveService _driveService;

    public CommandMessagingService(ITransitService transitService, IDriveService driveService)
    {
        _transitService = transitService;
        _driveService = driveService;
    }

    public async Task<CommandMessageResult> SendCommandMessage(CommandMessage command, byte[] transferIv)
    {
        Guard.Argument(command, nameof(command)).NotNull().Require(m => m.IsValid());

        var driveId = (await _driveService.GetDriveIdByAlias(command.Drive, true)).GetValueOrDefault();
        var internalFile = _driveService.CreateInternalFileId(driveId);

        await _driveService.WriteFileHeader(internalFile, new ServerFileHeader()
        {
            EncryptedKeyHeader = await this.EncryptKeyHeader(file, keyHeader),
            FileMetadata = new FileMetadata(internalFile)
            {
            },
            ServerMetadata = new ServerMetadata()
            {
                AccessControlList = AccessControlList.NewOwnerOnly
            }
        });

        var uploadResult = await _transitService.SendFile(internalFile, new TransitOptions()
        {
            IsTransient = true,
            Recipients = command.Recipients,
            UseCrossReference = false
        });

        return new CommandMessageResult()
        {
            RecipientStatus = uploadResult.RecipientStatus
        };
    }

    /// <summary>
    /// Gets a list of commands ready to be processed along with their associated files
    /// </summary>
    /// <returns></returns>
    public async Task<IEnumerable<object>> GetUnprocessedCommands()
    {
        return null;
    }

    public async Task MarkCommandsProcessed(IEnumerable<Guid> commandIds)
    {
    }

    private void AssertValidInstructionSet(UploadInstructionSet instructionSet)
    {
        Guard.Argument(instructionSet, nameof(instructionSet)).NotNull();

        if (null == instructionSet?.TransferIv || ByteArrayUtil.EquiByteArrayCompare(instructionSet.TransferIv, Guid.Empty.ToByteArray()))
        {
            throw new UploadException("Invalid or missing instruction set or transfer initialization vector");
        }

        if (instructionSet.TransitOptions?.Recipients?.Contains(_tenantContext.HostDotYouId) ?? false)
        {
            throw new UploadException("Cannot transfer to yourself; what's the point?");
        }

        if (!instructionSet.StorageOptions?.Drive?.IsValid() ?? false)
        {
            throw new UploadException("Target drive is invalid");
        }
    }
}