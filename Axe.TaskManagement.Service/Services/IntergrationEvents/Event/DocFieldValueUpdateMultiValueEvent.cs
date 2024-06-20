using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class DocFieldValueUpdateMultiValueEvent
    {
        public List<ItemDocFieldValueUpdateValue> ItemDocFieldValueUpdateValues { get; set; }
    }
}
