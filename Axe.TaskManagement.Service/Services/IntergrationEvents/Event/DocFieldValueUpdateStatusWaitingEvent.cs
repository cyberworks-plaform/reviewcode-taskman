using Ce.EventBus.Lib.Events;
using System;
using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    [Obsolete("Core refactor: Not using DocFieldValue table anymore from version 2.0", true)]
    public class DocFieldValueUpdateStatusWaitingEvent : IntegrationEvent
    {
        public List<Guid> DocFieldValueInstanceIds { get; set; }
    }
}
