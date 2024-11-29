using Ce.Constant.Lib.Dtos;
using System;
using System.ComponentModel;

namespace Axe.TaskManagement.Service.Dtos
{
    public class ExtendedMessagePriorityConfigDto : MessagePriorityConfig
    {
        [Description("Id Project")]
        public int ProjectId { get; set; }

        public Guid ProjectInstanceId { get; set; }
    }
}
