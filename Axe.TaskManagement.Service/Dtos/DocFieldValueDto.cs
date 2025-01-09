using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Interfaces;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Axe.TaskManagement.Service.Dtos
{
    [Obsolete("Need refactor - Core service: remove DocFieldValue",true)]
    [Description("Dữ liệu các trường của tài liệu thu thập")]
    public class DocFieldValueDto : Auditable<long>, IInstanceId, ISwitchable, IStatusable, IMultiTenant, IDeletable
    {
        #region properties

        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Description("Khóa chính")]
        public override long Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid InstanceId { get; set; }

        [Description("ID tài liệu")]
        [Required]
        public long DocId { get; set; }

        [Description("InstanceId tài liệu")]
        public Guid DocInstanceId { get; set; }

        [Description("ID trường dữ liệu")]
        [Required]
        public long DocTypeFieldId { get; set; }


        [Description("InstanceId trường dữ liệu")]
        public Guid DocTypeFieldInstanceId { get; set; }

        [Description("Giá trị")]
        public string Value { get; set; }   // Giá trị sau khi OCR, Nhập liệu, Kiểm tra

        [Description("Mã action")]
        [MaxLength(16)]
        public string ActionCode { get; set; }  // Mã action hiện tại (tương ứng với Value)

        [Description("File instanceId")]
        public Guid? FileInstanceId { get; set; }  // Dư thừa dữ liệu, lấy từ Doc tương ứng

        [Description("File part instanceId")]
        public Guid? FilePartInstanceId { get; set; }   // File Part = FileInstanceId (table Doc) + CoordinateArea

        [Description("Vùng toạ độ")]
        [MaxLength(128)]
        public string CoordinateArea { get; set; }  // Dư thừa dữ liệu, lấy từ DocTypeField tương ứng

        [Description("ID DocFieldValue nguồn?")]
        public long? CloneFrom { get; set; }

        public int? ContractorId { get; set; }  // Dư thừa dữ liệu, lấy từ DocTypeField tương ứng

        public int? ProjectId { get; set; }     // Dư thừa dữ liệu, lấy từ DocTypeField tương ứng

        public bool IsDeletable { get; set; } = true;

        public int TenantId { get; set; }

        public short Status { get; set; } = 1;

        public bool IsActive { get; set; } = true;

        #endregion

        public string DocCode { get; set; }

        public string DocName { get; set; }

        public string DocTypeFieldCode { get; set; }

        public string DocTypeFieldName { get; set; }

        public short DocStatus { get; set; }

        public bool IsShowValue { get; set; } = true;//trạng thái hiển thị dữ liệu khi có ít nhất 1 bước hoàn thành

        public string Note { get; set; }
    }
}
