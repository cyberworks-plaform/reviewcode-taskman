using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Axe.TaskManagement.Service.Dtos
{
    public class BatchDto : Auditable<int>, IInstanceId, ISwitchable, IHasName
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

        [Description("Tên lô")]
        [MaxLength(256)]
        public string Name { get; set; }                    // Số hiệu lô (1, 2, 3, ...n)

        public string Path { get; set; }                    // Lưu path SyncMetaId theo định dạng Id1/Id2/Id3

        [Description("Id Project")]
        public int ProjectId { get; set; }                  // Dư thừa dữ liệu

        [Description("InstanceId Project")]
        public Guid ProjectInstanceId { get; set; }          // Dư thừa dữ liệu

        public int DocCount { get; set; }                   // Dư thừa dữ liệu: Số lượng Doc hiện có trong lô (nếu doc bị xóa thì số lượng này dữ nguyên, ko cần phải giảm)

        public List<Guid> DocInstanceIds { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
