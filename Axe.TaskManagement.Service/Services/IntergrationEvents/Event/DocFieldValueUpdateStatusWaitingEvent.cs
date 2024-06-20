using Ce.EventBus.Lib.Events;
using System;
using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class DocFieldValueUpdateStatusWaitingEvent : IntegrationEvent
    {
        public List<Guid> DocFieldValueInstanceIds { get; set; }
    }
}
