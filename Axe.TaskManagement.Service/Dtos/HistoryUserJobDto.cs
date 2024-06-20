using System;
using System.Collections.Generic;
using System.Text;

namespace Axe.TaskManagement.Service.Dtos
{
    public class HistoryUserJobDto
    {
        public Guid UserInstanceId { get; set; }
        public string Name { get; set; }
        public int TotalJob { get; set; }
    }
}
