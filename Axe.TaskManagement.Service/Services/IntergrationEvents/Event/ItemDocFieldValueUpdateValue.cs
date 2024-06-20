using System;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class ItemDocFieldValueUpdateValue
    {
        public Guid InstanceId { get; set; }

        public string Value { get; set; }

        public string CoordinateArea { get; set; }

        public string ActionCode { get; set; }
    }
}
