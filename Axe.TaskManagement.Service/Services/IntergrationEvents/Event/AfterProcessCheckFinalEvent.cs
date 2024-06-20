using Axe.TaskManagement.Service.Dtos;
using Ce.EventBus.Lib.Events;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class AfterProcessCheckFinalEvent : IntegrationEvent
    {
        public JobDto Job { get; set; }

        public string AccessToken { get; set; }
    }
}
