using Axe.Utility.Enums;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Attributes;
using Ce.Common.Lib.Interfaces;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Axe.TaskManagement.Service.Dtos
{
    public class DocDto : Auditable<long>, IInstanceId, ISwitchable, IStatusable, IMultiTenant, IHasCode, IDeletable
    {
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Description("Khóa chính")]
        public override long Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid InstanceId { get; set; }
        [Description("Tên tài liệu")]
        [MaxLength(256)]
        public string Name { get; set; }
        [Description("Mã tài liệu")]
        [MaxLength(32)]
        public string Code { get; set; }

        [Description("File instanceId")]
        public Guid? FileInstanceId { get; set; }

        [Column(TypeName = "decimal(18, 0)")]
        public decimal Size { get; set; }   // FileSize

        public Guid? OwnedId { get; set; }  // Uploader

        [Description("Id Project")]
        public int ProjectId { get; set; }

        public string Path { get; set; }                    // Lưu path SyncMetaId theo định dạng Id1/Id2/Id3

        public string SyncMetaRelationPath { get; set; }    // Lưu path SyncMetaRelationId theo định dạng Id1/Id2/Id3

        public long? SyncTypeId { get; set; }

        public Guid? SyncTypeInstanceId { get; set; }       // Dư thừa dữ liệu

        public int? DigitizedTemplateId { get; set; }

        public Guid? DigitizedTemplateInstanceId { get; set; }      // Dư thừa dữ liệu

        [MaxLength(64)]
        public string DigitizedTemplateCode { get; set; }      // Dư thừa dữ liệu

        [Description("Id Warehouse")]
        public long? WarehouseId { get; set; }

        [Description("Id Batch")]
        public int? BatchId { get; set; }

        [Description("Giá trị cuối")]
        public string FinalValue { get; set; }  // Tổng hợp các giá trị từ DocFieldValue và hoàn thành các Job

        public bool IsDeletable { get; set; } = true;

        public int TenantId { get; set; }

        public bool IsActive { get; set; } = true;

        [Description("Khóa file")]
        public bool IsLocked { get; set; } = false;

        public short Status { get; set; } = (short)EnumDoc.Status.Unprocessed;   // refer EnumDoc.Status: Chưa xử lý, Đang xử ký tự động, Chờ phân phối, Đang xử lý thủ công, Hoàn thành, Loại bỏ
        [Description("Sao chép từ DocId gốc")]
        public long CopyFromDocId { get; set; } // Đánh dấu xem Doc hiện tại copy ra từ DocId gốc nào
        public int RowOrder { get; set; } = 1; //Nêu 1 file image có nhiều row => mỗi row sẽ tương đương với 1 doc

        [NotMapped]
        public IFormFile File { get; set; }

        [NotMapped]
        public string FileDocument { get; set; }    // base64

        [NotMapped]
        public double PercentComplete { get; set; }

        [NotMapped]
        public string ProjectCode { get; set; }

        [NotMapped]
        public string PathName { get; set; }
    }
}