using Axe.Utility.Enums;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.ComponentModel;

namespace Axe.TaskManagement.Service.Dtos
{
    public class LogJobDto
    {
        #region properties
        [Description("Khóa chính")]
        [BsonId]
        public string Id { get; set; }

        [BsonElement("instance_id")]
        public Guid InstanceId { get; set; }

        [Description("InstanceId trường dữ liệu")]
        [BsonElement("doc_type_field_instance_id")]
        public Guid? DocTypeFieldInstanceId { get; set; }

        [Description("Kiểu nhập")]
        [BsonElement("input_type")]
        public short InputType { get; set; } = (short)EnumDocTypeField.InputType.InpText;

        [BsonElement("project_type_instance_id")]
        public Guid? ProjectTypeInstanceId { get; set; }

        [BsonElement("project_instance_id")]
        public Guid? ProjectInstanceId { get; set; }

        [BsonElement("work_flow_instance_id")]
        public Guid? WorkflowInstanceId { get; set; }

        [BsonElement("work_flow_step_instance_id")]
        public Guid? WorkflowStepInstanceId { get; set; }

        [BsonElement("action_code")]
        public string ActionCode { get; set; }

        [BsonElement("user_instance_id")]
        public Guid? UserInstanceId { get; set; }

        [BsonElement("tenant_id")]
        public int TenantId { get; set; }

        [BsonElement("status")]
        public short Status { get; set; } = (short)EnumJob.Status.Waiting;

        [BsonElement("created_date")]
        public DateTime? CreatedDate { get; set; } = DateTime.UtcNow;

        [BsonElement("created_by")]
        public Guid? CreatedBy { get; set; }

        [BsonElement("last_modification_date")]
        public DateTime? LastModificationDate { get; set; }

        [BsonElement("last_modified_by")]
        public Guid? LastModifiedBy { get; set; }

        [Description("Là công việc trong bước thuộc luồng song song?")]
        [BsonElement("is_parallel_job")]
        public bool IsParallelJob { get; set; } = false;

        [Description("Parallel instanceId")]
        [BsonElement("parallel_job_instance_id")]
        public Guid? ParallelJobInstanceId { get; set; }
        #endregion
    }
}
