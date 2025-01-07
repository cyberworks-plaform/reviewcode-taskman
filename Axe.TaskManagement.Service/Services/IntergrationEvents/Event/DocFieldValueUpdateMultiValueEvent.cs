using System;
using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    [Obsolete("Remove DocFieldValueUpdateMultiValueEvent",true)]
    public class DocFieldValueUpdateMultiValueEvent
    {
        public List<ItemDocFieldValueUpdateValue> ItemDocFieldValueUpdateValues { get; set; }
    }
}
