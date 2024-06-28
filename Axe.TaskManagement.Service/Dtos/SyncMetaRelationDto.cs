using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Interfaces;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel;
using System;
using System.ComponentModel.DataAnnotations;

namespace Axe.TaskManagement.Service.Dtos
{
    public class SyncMetaRelationDto : Entity<long>, IInstanceId
    {
        [Key]
        [Required]
        [Description("Khóa chính")]
        public override long Id { get; set; }

        [Required]
        [Description("ID Instance")]
        public Guid InstanceId { get; set; }

        [Required]
        public long SyncMetaId { get; set; }

        [Required]
        public Guid SyncMetaInstanceId { get; set; }

        public long? ParentSyncMetaId { get; set; }

        public Guid? ParentSyncMetaInstanceId { get; set; }     // Dư thừa dữ liệu

        public string Path { get; set; }      // Lưu path SyncMetaId theo định dạng Id1/Id2/Id3

        [Required]
        public int ProjectId { get; set; }

        [Required]
        public Guid ProjectInstanceId { get; set; }             // Dư thừa dữ liệu

        [Required]
        public long SyncMetaCategoryId { get; set; }

        [Required]
        public Guid SyncMetaCategoryInstanceId { get; set; }    // Dư thừa dữ liệu

        [Required]
        public long SyncTypeId { get; set; }

        [Required]
        public Guid SyncTypeInstanceId { get; set; }    // Dư thừa dữ liệu

        [Description("Khóa thư mục")]
        public bool IsLocked { get; set; } = false;

        [Description("Có đang upload dữ liệu không?")]
        public bool IsUploading { get; set; } = false;

        /// <summary>
        /// Tổng số file trong thư mục -> được tính toán realtime khi query
        /// </summary>
        public long TotalFile { get; set; }
    }
}