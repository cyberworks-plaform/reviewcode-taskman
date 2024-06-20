using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Dtos
{
    public class ConfirmAutoInputDto
    {
        public List<ItemConfirmAutoInputDto> data { get; set; }
    }

    public class ItemConfirmAutoInputDto
    {
        public int labeler_id { get; set; }

        public string label_value { get; set; }
    }
}
