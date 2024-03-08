using System;
using MediatR;
using Odin.Services.Authorization.Apps;

namespace Odin.Services.Mediator;

public class AppRegistrationChangedNotification : EventArgs, INotification
{
    public AppRegistration NewAppRegistration { get; set; }
    public AppRegistration OldAppRegistration { get; set; }
}