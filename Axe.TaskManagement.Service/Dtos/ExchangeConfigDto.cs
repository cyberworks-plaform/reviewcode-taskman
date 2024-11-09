using Ce.Constant.Lib.Enums;
using System;

namespace Axe.TaskManagement.Service.Dtos
{
    public class ExchangeConfigDto
    {
        public string ExchangeName { get; set; }

        public short TypeProcessing { get; set; } = (short)EnumEventBus.ConsumerTypeProcessing.ProcessImmediate;

        public TimeSpan TimeOut { get; set; } = TimeSpan.FromMinutes(30);
    }
}
