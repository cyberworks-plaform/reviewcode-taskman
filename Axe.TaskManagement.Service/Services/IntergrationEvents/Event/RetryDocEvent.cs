using Axe.TaskManagement.Service.Dtos;
using Ce.EventBus.Lib.Events;
using System;
using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class RetryDocEvent : IntegrationEvent
    {
        public Guid DocInstanceId { get; set; }                 // Dư thừa dữ liệu

        public List<JobDto> Jobs { get; set; }

        public string AccessToken { get; set; }
    }
}
