using Ce.Common.Lib.Abstractions;
using Ce.Constant.Lib.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Dtos
{
    [Description("Cấu hình dịch vụ bên ngoài")]
    public class ExternalProviderServiceConfigDto : Auditable<long>
    {
        [Required]
        [Description("Khóa chính")]
        public override long Id { get; set; }

        public int ExternalProviderId { get; set; }

        [Description("InstanceId của WorkflowStepType")]
        public Guid WorkflowStepTypeInstanceId { get; set; }

        [Description("Mã action")]
        [MaxLength(32)]
        public string ActionCode { get; set; }                      // Dư thừa dữ liệu

        [Description("InstanceId của DigitizedTemplate")]
        public Guid? DigitizedTemplateInstanceId { get; set; }      // Đối với mẫu số hóa hệ thống

        [Description("Code của DigitizedTemplate")]
        [MaxLength(64)]
        public string DigitizedTemplateCode { get; set; }           // Đối với mẫu số hóa hệ thống

        [MaxLength(2048)]
        public string ApiEndpoint { get; set; }

        public bool IsPrimary { get; set; } = true;

        public short HttpMethodType { get; set; } = (short)HttpClientMethodType.POST;

        public string ConfigHeader { get; set; }

        #region Result

        public string ServiceProviderName { get; set; }
        public string Domain { get; set; }
        #endregion
    }
}
