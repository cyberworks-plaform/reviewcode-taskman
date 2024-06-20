using Axe.Utility.Enums;
using Ce.Common.Lib.Interfaces;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.ComponentModel.DataAnnotations;

namespace Axe.TaskManagement.Service.Dtos
{
    public class QueueLockDto : IEntity<string>
    {
        [Key]
        [Required]
        [BsonId]
        [BsonElement("_id")]
        public string Id { get; set; }

        [BsonElement("file_instance_id")]
        public Guid? FileInstanceId { get; set; }   // Dư thừa dữ liệu

        [BsonElement("doc_instance_id")]
        public Guid? DocInstanceId { get; set; }    // Dư thừa dữ liệu

        [BsonElement("doc_name")]
        public string DocName { get; set; }     // Dư thừa dữ liệu

        [BsonElement("doc_created_date")]
        public DateTime? DocCreatedDate { get; set; }   // Dư thừa dữ liệu

        [BsonElement("doc_path")]
        public string DocPath { get; set; }             // Dư thừa dữ liệu

        [BsonElement("project_type_instance_id")]
        public Guid? ProjectTypeInstanceId { get; set; }     // Dư thừa dữ liệu

        [BsonElement("project_instance_id")]
        public Guid? ProjectInstanceId { get; set; }    // Dư thừa dữ liệu

        [BsonElement("digitized_template_instance_id")]
        public Guid? DigitizedTemplateInstanceId { get; set; }  // Dư thừa dữ liệu

        [BsonElement("digitized_template_code")]
        public string DigitizedTemplateCode { get; set; }   // Dư thừa dữ liệu

        [BsonElement("doc_type_field_instance_id")]
        public Guid? DocTypeFieldInstanceId { get; set; }

        [BsonElement("doc_type_field_sort_order")]
        public int DocTypeFieldSortOrder { get; set; }    // Dư thừa dữ liệu

        [BsonElement("doc_field_value_instance_id")]
        public Guid? DocFieldValueInstanceId { get; set; }

        [BsonElement("workflow_instance_id")]
        public Guid? WorkflowInstanceId { get; set; }   // Dư thừa dữ liệu

        public Guid? WorkflowStepInstanceId { get; set; }   // Dư thừa dữ liệu

        public string ActionCode { get; set; }  // Dư thừa dữ liệu

        [BsonElement("input_param")]
        public string InputParam { get; set; }

        [BsonElement("status")]
        public short Status { get; set; } = (short)EnumQueue.Status.Waitting;
    }
}
