using System;
using System.Collections.Generic;
using System.Text;

namespace Axe.TaskManagement.Service.Dtos
{
    public class SelectItemChartDto
    {
        public string FieldId { get; set; }
        public string FieldName { get; set; }
        public float Value { get; set; }
    }
}
