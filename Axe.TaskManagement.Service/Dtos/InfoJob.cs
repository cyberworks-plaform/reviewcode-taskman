using System;
using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Dtos
{
    public class InfoJob
    {
        public Guid ProjectTypeInstanceId { get; set; }

        public string ProjectTypeName { get; set; }

        public List<InfoJobItem> InfoJobItems { get; set; }
    }
}
