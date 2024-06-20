using System;
using Ce.Common.Lib.Interfaces;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Axe.Utility.Enums;

namespace Axe.TaskManagement.Model.Entities
{
    [Table("Tasks")]
    [Description("Task")]
    public class TaskEntity : IEntity<ObjectId>, IStatusable
    {
        [Key]
        [Required]
        [BsonId]
        [BsonElement("_id")]
        public ObjectId Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [BsonElement("instance_id")]
        public Guid InstanceId { get; set; }

        [BsonElement("file_instance_id")]
        public Guid? FileInstanceId { get; set; }

        [BsonElement("doc_instance_id")]
        public Guid? DocInstanceId { get; set; }

        [BsonElement("doc_name")]
        public string DocName { get; set; }     // Dư thừa dữ liệu

        [BsonElement("doc_created_date")]
        public DateTime? DocCreatedDate { get; set; }   // Thời gian tạo tài liệu

        [BsonElement("project_type_instance_id")]
        public Guid? ProjectTypeInstanceId { get; set; }     // Dư thừa dữ liệu

        [BsonElement("project_instance_id")]
        public Guid? ProjectInstanceId { get; set; }

        [BsonElement("digitized_template_instance_id")]
        public Guid? DigitizedTemplateInstanceId { get; set; }

        [BsonElement("workflow_instance_id")]
        public Guid? WorkflowInstanceId { get; set; }

        [BsonElement("progress")]
        public string Progress { get; set; }    // Tiến độ công việc theo cấu hình bước, dạng json lưu trữ List<TaskStepProgress>

        [BsonElement("workflow_schema-infoes")]
        public string WorkflowSchemaInfoes { get; set; }    // Thông tin về dây, dạng json lưu trữ List<WorkflowSchemaConditionInfo>

        [BsonElement("status")]
        public short Status { get; set; } = (short)EnumTask.Status.Created;
    }
}
