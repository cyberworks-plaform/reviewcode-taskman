using Axe.Utility.Enums;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Interfaces;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Axe.TaskManagement.Service.Dtos
{
    [Description("Các trường mở rộng của loại mẫu số hoá")]
    public class DocTypeFieldDto : Auditable<long>, IInstanceId, ISwitchable, IMultiTenant, IHasCode, IHasName, IDeletable
    {
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Description("Khóa chính")]
        public override long Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid InstanceId { get; set; }

        [Description("Kiểu nhập")]
        [Required]
        public short InputType { get; set; } = (short)EnumDocTypeField.InputType.InpText;  // refer EnumDocTypeField.InputType

        [Description("Id danh mục riêng")]
        public long? PrivateCategoryId { get; set; } // for InputType is PrivateCategory

        [Description("InstanceId danh mục riêng")]
        public Guid? PrivateCategoryInstanceId { get; set; } // Dư thừa dữ liệu

        public bool? IsMultipleSelection { get; set; } // Cho phép chọn nhiều trong trường hợp InputType là PrivateCategory

        [Description("Tên trường")]
        [Required]
        [MaxLength(250)]
        public string Name { get; set; }

        [Description("Mã trường")]
        [MaxLength(128)]
        public string Code { get; set; }

        [Description("Định dạng")]
        [MaxLength(30)]
        public string Format { get; set; }  // for date field, number field, ex: dd/MM/yyyy

        [Description("Thứ tự")]
        public int? SortOrder { get; set; }

        [Description("Bắt buộc?")]
        public bool IsRequire { get; set; } = false;

        [Description("Hiển thị trên grid")]
        public bool IsShowGrid { get; set; } = true;

        [Description("Cho phép tìm kiếm trên grid")]
        public bool IsSearchGrid { get; set; }

        [Description("Độ dài tối thiểu")]
        public int MinLength { get; set; }

        [Description("Độ dài tối đa")]
        public int MaxLength { get; set; }

        [Description("Giá trị tối thiểu")]
        public decimal? MaxValue { get; set; }

        [Description("Giá trị tối đa")]
        public decimal? MinValue { get; set; }

        public int? DocTypeFieldRelatedId { get; set; } // for related with othor DocTypeField

        public short? DocTypeFieldRelatedOperator { get; set; } // refer enum OperatorType

        [Description("File part instanceId")]
        public Guid? FilePartInstanceId { get; set; }   // File Part = FileInstanceId (table Project) + CoordinateArea

        [Description("Vùng toạ độ")]
        [MaxLength(1024)]
        public string CoordinateArea { get; set; }

        public int? ProjectId { get; set; }

        public Guid? ProjectInstanceId { get; set; }

        public int? DigitizedTemplateId { get; set; }

        public Guid? DigitizedTemplateInstanceId { get; set; }      // Dư thừa dữ liệu

        public bool IsFromSystem { get; set; } = false;

        public bool IsDeletable { get; set; } = true;

        public int TenantId { get; set; }

        public bool IsActive { get; set; } = true;
        [DefaultValue(true)]
        public bool ShowForInput { get; set; } = true; //Cho phép thiết lập ẩn / hiện thị để nhập liệu

        public string InputShortNote { get; set; }
    }
}
