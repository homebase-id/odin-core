using System.Collections.Generic;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Tests.AppNotifications;

/// <summary>
/// The socket handshake's options: malformed input is the client's error, never an unhandled server one
/// </summary>
public class EstablishConnectionOptionsTests
{
    [Test]
    public void AnUnknownShapeNameIsAClientError()
    {
        var e = Assert.Throws<OdinClientException>(() => EstablishConnectionOptions.Parse("{\"drives\":[],\"resultShape\":\"slim\"}"));
        Assert.That(e!.ErrorCode, Is.EqualTo(OdinClientErrorCode.InvalidQuery), e.Message);
    }

    [Test]
    public void AnUnknownShapeNumberIsAClientErrorAndChangesNothing()
    {
        var socket = new DeviceSocket();
        var options = EstablishConnectionOptions.Parse("{\"drives\":[],\"resultShape\":7}");

        var e = Assert.Throws<OdinClientException>(() => options.ApplyTo(socket, NoPermissions()));

        Assert.That((e!.ErrorCode, socket.ResultShape, socket.Drives.Count), Is.EqualTo((OdinClientErrorCode.InvalidQuery, FileResultShape.Full, 0)));
    }

    [Test]
    public void NullDrivesSubscribeToNothing()
    {
        var socket = new DeviceSocket();
        EstablishConnectionOptions.Parse("{\"drives\":null,\"resultShape\":\"minimal\"}").ApplyTo(socket, NoPermissions());

        Assert.That((socket.ResultShape, socket.Drives.Count), Is.EqualTo((FileResultShape.Minimal, 0)));
    }

    [Test]
    public void NoShapeMeansFullHeaders()
    {
        var socket = new DeviceSocket { ResultShape = FileResultShape.Minimal };
        EstablishConnectionOptions.Parse("{\"drives\":[]}").ApplyTo(socket, NoPermissions());

        Assert.That(socket.ResultShape, Is.EqualTo(FileResultShape.Full));
    }

    private static PermissionContext NoPermissions() => new(new Dictionary<string, PermissionGroup>(), null);
}
