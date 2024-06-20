using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Dtos
{
    public class ConfirmAutoOutputDto
    {
        public string true_label { get; set; }

        public double confidence { get; set; }

        public List<ItemLabelConfidence> labelers { get; set; }

        public List<ItemWordConfidence> words { get; set; }
    }

    public class ItemLabelConfidence
    {
        public int labeler_id { get; set; }

        public string label_value { get; set; }

        public double accuracy { get; set; }

        public double different_point { get; set; }

        public double confidence { get; set; }

        public double secondary_different_point { get; set; }

        public double secondary_confidence { get; set; }
    }

    public class ItemWordConfidence
    {
        public int idx { get; set; }

        public string value { get; set; }

        public double accuracy { get; set; }

        public double different_point { get; set; }

        public double confidence { get; set; }
    }
}
