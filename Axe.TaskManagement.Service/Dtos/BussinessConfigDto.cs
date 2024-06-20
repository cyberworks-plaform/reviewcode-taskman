using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Interfaces;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Axe.TaskManagement.Service.Dtos
{
    public class BussinessConfigDto : Auditable<int>, IInstanceId, ISwitchable, IMultiTenant, IHasName, IHasCode
    {
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public override int Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid InstanceId { get; set; }

        [Required]
        [MaxLength(64)]
        [Description("Tên của tham số")]
        public string Name { get; set; }

        [Required]
        [Column(TypeName = "varchar(32)")]
        [MaxLength(32)]
        [Description("Mã của tham số")]
        public string Code { get; set; }

        [MaxLength(5000)]
        [Description("Giá trị string của tham số")]
        public string StringVal { get; set; }

        [Description("Giá trị DateTime của tham số")]
        public DateTime? DateTimeVal { get; set; }

        [Description("Giá trị Int của tham số")]
        public long? IntVal { get; set; }

        [Description("Giá trị Float của tham số")]
        public float? FloatVal { get; set; }

        [Description("Giá trị Bool của tham số")]
        public bool? BoolVal { get; set; }

        [Description("Id của dự án")]
        public int ProjectId { get; set; }

        [MaxLength(300)]
        [Description("Mô tả của tham số")]
        public string Description { get; set; }

        public bool IsShow { get; set; } = true;

        public int TenantId { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
