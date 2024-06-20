using Axe.TaskManagement.Service.Dtos;
using Ce.EventBus.Lib.Events;
using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class LogJobEvent : IntegrationEvent
    {
        public List<LogJobDto> LogJobs { get; set; }

        public string AccessToken { get; set; }
    }
}
