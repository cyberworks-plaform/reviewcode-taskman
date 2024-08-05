using Axe.Utility.Enums;
using Ce.Common.Lib.Interfaces;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Axe.TaskManagement.Model.Entities
{
    [Table("Complains")]
    [Description("Khiếu nại")]
    public class Complain : IEntity<ObjectId>, IAuditable, IInstanceId, IStatusable/*, IMultiTenant*/, ICloneable
    {
        #region properties

        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Description("Khóa chính")]
        [BsonId]
        public ObjectId Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [BsonElement("instance_id")]
        public Guid InstanceId { get; set; }

        [Description("Mã khiếu nại")]
        [MaxLength(32)]
        [BsonElement("code")]
        public string Code { get; set; }

        [Description("InstanceId công việc")]
        [BsonElement("job_instance_id")]
        public Guid? JobInstanceId { get; set; }

        [Description("Mã công việc")]
        [MaxLength(32)]
        [BsonElement("job_code")]
        public string JobCode { get; set; }

        [Description("InstanceId tài liệu")]
        [BsonElement("doc_instance_id")]
        public Guid? DocInstanceId { get; set; }

        [BsonElement("project_instance_id")]
        public Guid? ProjectInstanceId { get; set; }   // Dư thừa dữ liệu

        [BsonElement("work_flow_instance_id")]
        public Guid? WorkflowInstanceId { get; set; }   // Dư thừa dữ liệu

        [BsonElement("work_flow_step_instance_id")]
        public Guid? WorkflowStepInstanceId { get; set; }   // Thuộc bước nào

        [BsonElement("action_code")]
        public string ActionCode { get; set; }   // refer ActionCodeConstants: Nhập liệu, Kiểm tra, Đối chiếu, CheckFinal, Dư thừa dữ liệu

        [BsonElement("user_instance_id")]
        public Guid? UserInstanceId { get; set; }   // User xử lý dữ liệu nào đang đảm nhiệm

        [Description("InstanceId trường dữ liệu")]
        [BsonElement("doc_type_field_instance_id")]
        public Guid? DocTypeFieldInstanceId { get; set; }

        [Description("InstanceId trường giá trị dữ liệu")]
        [BsonElement("doc_field_value_instance_id")]
        public Guid? DocFieldValueInstanceId { get; set; }   // Dư thừa dữ liệu, được link 1-1 đến DocFieldValue nào

        [BsonElement("created_date")]
        public DateTime? CreatedDate { get; set; } = DateTime.UtcNow;

        [BsonElement("created_by")]
        public Guid? CreatedBy { get; set; }

        [BsonElement("last_modification_date")]
        public DateTime? LastModificationDate { get; set; }

        [BsonElement("last_modified_by")]
        public Guid? LastModifiedBy { get; set; }

        [Description("Ghi chú")]
        //[MaxLength(128)]
        [BsonElement("note")]
        public string Note { get; set; }

        [Description("Có cập nhật value")]
        [BsonElement("has_change")]
        public bool HasChange { get; set; } = false;        // Có thay đổi giá trị so với bước TRƯỚC hay ko

        [Description("Giá trị")]
        [BsonElement("value")]
        public string Value { get; set; }

        [Description("Old value lưu giá trị trước thay đổi")]
        [BsonElement("old_value")]
        public string OldValue { get; set; }                // Lưu trữ giá trị của bước TRƯỚC trong trường hợp HasChange = true, nếu HasChange = false thì OldValue = null

        [Description("CompareValue lưu giá trị so sánh trước thay đổi")]
        [BsonElement("compare_value")]
        public string CompareValue { get; set; }

        [BsonElement("right_status")]
        public short RightStatus { get; set; }

        [BsonElement("status")]
        public short Status { get; set; }

        [BsonElement("choose_value")]
        public short ChooseValue { get; set; }     // Refer: EnumComplain.ChooseValue

        #endregion

        public object Clone()
        {
            return MemberwiseClone();
        }
    }
}
