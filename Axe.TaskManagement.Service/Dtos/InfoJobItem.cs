using Ce.Constant.Lib.Enums;

namespace Axe.TaskManagement.Service.Dtos
{
    public class InfoJobItem
    {
        public string WorkflowStepName { get; set; }

        public bool HasJob { get; set; }

        public string ViewUrl { get; set; } // Job/DataEntry

        public string ServiceCode { get; set; }

        public string ApiEndpoint { get; set; }         // Api endpoint

        public short HttpMethodType { get; set; } = (short)HttpClientMethodType.POST;

        public string Icon { get; set; }
        public string ActionCode { get; set; }
    }
}
