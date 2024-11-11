using Ce.EventBus.Lib.Events;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class TaskEvent : IntegrationEvent
    {
        public string Input { get; set; }

        public string Output { get; set; }

        public bool IsRetry { get; set; }

        public string AccessToken { get; set; }
    }
}
