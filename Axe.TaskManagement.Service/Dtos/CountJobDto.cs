using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Dtos
{
    public class CountJobDto
    {
        public long CountIgnore { get; set; }  // Số lượng job bị bỏ qua
        public long CountBounced { get; set; } // Số lượng job bị qa trả về
    }
}
