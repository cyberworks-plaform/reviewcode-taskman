using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Dtos
{
    public class SummaryRequestDto
    {
        public string PathIds { get; set; }
        public string SyncMetaPaths { get; set; }
    }
}
