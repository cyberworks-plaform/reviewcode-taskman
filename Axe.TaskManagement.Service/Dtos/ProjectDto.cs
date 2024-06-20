using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Interfaces;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Axe.Utility.Enums;
using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Dtos
{
    public class ProjectDto : Auditable<int>, IInstanceId, IMultiTenant, IStatusable, IHasCode, IHasName, ISwitchable, IDeletable
    {
        #region ProjectInfo
        [Description("Khóa chính")]
        public override int Id { get; set; }

        [Description("ID Instance")]
        public Guid InstanceId { get; set; }

        #region Thông tin dự án

        [Description("Mã dự án")]
        [Required(ErrorMessage = "{0} không được để trống")]
        [Display(Name = "Mã dự án", Prompt = "Mã dự án")]
        public string Code { get; set; }

        [Description("Tên dự án")]
        [Required(ErrorMessage = "{0} không được để trống")]
        [Display(Name = "Tên dự án", Prompt = "Tên dự án")]
        public string Name { get; set; }


        [Description("ID nhà thầu")]
        [Required(ErrorMessage = "{0} không được để trống")]
        [Display(Name = "Nhà thầu", Prompt = "Nhà thầu")]
        public int ContractorId { get; set; }

        [Description("ID khách hàng")]
        [Required(ErrorMessage = "{0} không được để trống")]
        [Display(Name = "Khách hàng", Prompt = "Khách hàng")]
        public int CustomerId { get; set; }

        [Description("Ngày bắt đầu")]
        [Required(ErrorMessage = "{0} không được để trống")]
        [Display(Name = "Ngày bắt đầu", Prompt = "Ngày bắt đầu")]
        public DateTime StartDate { get; set; }

        [Description("Ngày kết thúc")]
        [Required(ErrorMessage = "{0} không được để trống")]
        [Display(Name = "Ngày kết thúc", Prompt = "Ngày kết thúc")]
        public DateTime EndDate { get; set; }

        [Description("Nội dung dự án")]
        public string ProjectContent { get; set; }

        [Description("ID mẫu hố hóa")]
        public int DocTypeId { get; set; } = 0;

        [Description("File instanceId")]
        public Guid? FileInstanceId { get; set; }   // File template

        [Description("Instance quy trình")]
        public Guid? WorkflowInstanceId { get; set; }

        [Description("Dự án mặc định của nhà thầu")]
        public bool IsDefault { get; set; } = false;

        public bool IsDeletable { get; set; } = true;

        public int ProjectTypeId { get; set; }

        public Guid ProjectTypeInstanceId { get; set; }

        #endregion Thông tin dự án

        public int TenantId { get; set; }

        public short Status { get; set; } = (short)EnumProject.Status.Processing;

        public bool IsActive { get; set; } = true;

        [Description("Kiểu dữ liệu Dự án")]
        public short DataType { get; set; } // refer EnumDocType.Type

        [Description("Tên mẫu số hóa")]
        public string DocTypeName { get; set; }

        public double PercentDone { get; set; } //=> % hoàn thành

        public long CountDoc { get; set; }
        public long CountDocDone { get; set; }

        public long DataEntryCount { get; set; }
        public long DataEntryDone { get; set; }
        public long DataCheckCount { get; set; }
        public long DataCheckDone { get; set; }
        public long CheckFinalCount { get; set; }
        public long CheckFinalDone { get; set; }

        public long DataEntryFileCount { get; set; }
        public long DataEntryFileDone { get; set; }
        public long DataCheckFileCount { get; set; }
        public long DataCheckFileDone { get; set; }
        public long CheckFinalFileCount { get; set; }
        public long CheckFinalFileDone { get; set; }

        #endregion

        #region CustomerInfo
        [Description("Tên khách hàng")]
        public string CustomerName { get; set; }

        [Description("Email khách hàng")]
        public string CustomerEmail { get; set; }

        [Description("Tỉnh/Thành phố")]
        public Guid? CustomerProvinceInstanceId { get; set; }

        [Description("Quận/Huyện")]
        public Guid? CustomerDistrictInstanceId { get; set; }

        [Description("Xã/Phường/Thị Trấn")]
        public Guid? CustomerWardInstanceId { get; set; }

        [Description("Địa chỉ chi tiết")]
        public string CustomerDetailedAddress { get; set; }


        [Description("Loại đối tượng")]
        public int CustomerProjectTargetId { get; set; }

        [Description("User đại diện")]
        public Guid? CustomerRepresentativeUserInstanceId { get; set; } //=> dai dien

        [Description("File khách hàng")]
        public Guid? CustomerFileInstanceId { get; set; }

        [Description("Ghi chú khách hàng")]
        public string CustomerNote { get; set; }
        #endregion

        #region Contractor
        [Description("Tên nhà thầu")]
        public string ContractorName { get; set; }

        [Description("Số điện thoại nhà thầu")]
        public string ContractorPhone { get; set; }

        [Description("Email nhà thầu")]
        public string ContractorEmail { get; set; }

        [Description("Province nhà thầu")]
        public Guid? ContractorProvinceInstanceId { get; set; }

        [Description("District nhà thầu")]
        public Guid? ContractorDistrictInstanceId { get; set; }

        [Description("Ward nhà thầu")]
        public Guid? ContractorWardInstanceId { get; set; }

        #endregion

        #region WorkingFolder
        public List<ProjectActionCode> ActionCodes { get; set; } = new List<ProjectActionCode>();
        public int TotalJob { get; set; }
        #endregion
    }

    public class ProjectActionCode
    {
        public long Id { get; set; }
        public Guid InstanceId { get; set; }
        public string Name { get; set; }
        public string ActionCode { get; set; }
        public Guid WorkflowInstanceId { get; set; }
        public int TotalJob { get; set; }
        public bool IsChooseProcessingUser { get; set; } = false;
        public string ConfigStep { get; set; }
        public ConfigStepProperty ConfigStepProperty { get; set; } = new ConfigStepProperty();
        public List<InputTypeGroup> InputTypeGroups { get; set; } = new List<InputTypeGroup>();
    }

    public class ConfigStepProperty
    {
        public bool IsChooseProcessingUser { get; set; }
        public int MaxTimeProcessing { get; set; }
        public int NumOfJobDistributed { get; set; }
        public bool IsShareJob { get; set; }
        public int NumOfResourceInJob { get; set; }
        public int NumOfResourceCorrectData { get; set; }
        public string RateOfReceivingData { get; set; }
        public string IsReceiveDataIgnore { get; set; }
        public string Labeling { get; set; }
        public string AssignmentForms { get; set; }
        public bool IsPaidStep { get; set; }
        public string IsChooseOcrServiceProvider { get; set; }
        public string ConfigStepOcrServiceProviders { get; set; }
        public string ConditionType { get; set; }
        public string ConfigStepConditions { get; set; }
        public bool IsPause { get; set; }
        public string Description { get; set; }
        public List<ConfigStepUsers> UserList { get; set; } = new List<ConfigStepUsers>();
    }
    public class ConfigStepUsers
    {
        public Guid? UserInstanceId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int Status { get; set; }
    }
    public class InputTypeGroup
    {
        public string MetaKey { get; set; }
        public int InputType { get; set; }
        public int InputTypeTotalJob { get; set; }
        public List<DocTypeField> DocTypeFields { get; set; } = new List<DocTypeField>();
    }

    public class DocTypeField
    {
        public string DocTypeFieldName { get; set; }
        public string DocTypeFieldCode { get; set; }
        public Guid DocTypeFieldInstanceId { get; set; }
        public int DocTypeFieldTotalJob { get; set; }
    }
}
