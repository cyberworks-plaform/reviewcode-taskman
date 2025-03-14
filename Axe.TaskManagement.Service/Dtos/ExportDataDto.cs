using Axe.TaskManagement.Model.Enums;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Axe.TaskManagement.Service.Dtos
{
    [Table("ExportDatas")]
    [Description("Xuất dữ liệu")]
    public class ExportDataDto : Auditable<int>, IInstanceId, IHasName, IStatusable
    {
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Description("Khóa chính")]
        public override int Id { get; set; }

        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Description("ID Instance")]
        public Guid InstanceId { get; set; }

        [MaxLength(256)]
        public string Name { get; set; }

        [MaxLength(1000)]
        public string FilePath { get; set; }

        [Column(TypeName = "decimal(18, 0)")]
        public decimal Size { get; set; }   // FileSize

        public string SyncMetaRelationPath { get; set; }    // Lưu path SyncMetaRelationId theo định dạng Id1/Id2/Id3

        [Description("Id Project")]
        public int ProjectId { get; set; }

        [Description("InstanceId Project")]
        public Guid ProjectInstanceId { get; set; }          // Dư thừa dữ liệu

        public string DigitizedTemplateIds { get; set; }

        public bool IsExportFolder { get; set; }

        public short Type { get; set; } = (short)EnumExportData.Type.Excel;

        [Column(TypeName = "decimal(18, 2)")]
        public decimal Progress { get; set; }

        [Description("Ghi chú")]
        [MaxLength(512)]
        public string Note { get; set; }

        public short Status { get; set; } = (short)EnumExportData.Status.New;
        public string Request { get; set; } // Dùng cho kết xuất report
    }
}
