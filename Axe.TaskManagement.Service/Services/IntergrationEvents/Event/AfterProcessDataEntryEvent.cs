using System.Collections.Generic;
using Axe.TaskManagement.Service.Dtos;
using Ce.EventBus.Lib.Events;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class AfterProcessDataEntryEvent : IntegrationEvent
    {
        public List<JobDto> Jobs { get; set; }

        public string AccessToken { get; set; }
    }
}
