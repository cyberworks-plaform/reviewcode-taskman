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
    [Table("Jobs")]
    [Description("Công việc")]
    public class Job : IEntity<ObjectId>, IAuditable, IInstanceId, IStatusable, IMultiTenant, ICloneable
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

        [Description("Mã công việc")]
        [MaxLength(32)]
        [BsonElement("code")]
        public string Code { get; set; }

        [Description("InstanceId lượt nhận công việc")]
        [BsonElement("turn_instance_id")]
        public Guid? TurnInstanceId { get; set; }

        [Description("InstanceId mẫu số hóa")]
        [BsonElement("digitized_template_instance_id")]
        public Guid? DigitizedTemplateInstanceId { get; set; }

        [Description("Code mẫu số hóa")]
        [BsonElement("digitized_template_code")]
        public string DigitizedTemplateCode { get; set; }   // Dư thừa dữ liệu

        [Description("Id task")]
        [Required]
        [BsonElement("task_id")]
        public ObjectId TaskId { get; set; }

        [Description("InstanceId task")]
        [Required]
        [BsonElement("task_instance_id")]
        public Guid? TaskInstanceId { get; set; }

        [Description("InstanceId tài liệu")]
        [Required]
        [BsonElement("doc_instance_id")]
        public Guid? DocInstanceId { get; set; }

        [Description("Tên tài liệu")]
        [MaxLength(256)]
        [BsonElement("doc_name")]
        public string DocName { get; set; }   // Dư thừa dữ liệu, phục vụ cho bước CheckFinal

        [BsonElement("doc_created_date")]
        public DateTime? DocCreatedDate { get; set; }   // Thời gian tạo tài liệu

        [Description("InstanceId kiểu đồng bộ")]
        [BsonElement("sync_type_instance_id")]
        public Guid? SyncTypeInstanceId { get; set; }   // InstanceId kiểu đồng bộ

        [Description("Đường dẫn tài liệu")]
        [BsonElement("doc_path")]
        public string DocPath { get; set; }   // Đường dẫn tài liệu

        [Description("Id thư mục cha")]
        [BsonElement("sync_meta_id")]
        public long SyncMetaId { get; set; }   // Id thư mục cha

        [Description("InstanceId trường dữ liệu")]
        [BsonElement("doc_type_field_instance_id")]
        public Guid? DocTypeFieldInstanceId { get; set; }

        [Description("Mã trường dữ liệu")]
        [BsonElement("doc_type_field_code")]
        public string DocTypeFieldCode { get; set; }

        [Description("Tên trường dữ liệu")]
        [BsonElement("doc_type_field_name")]
        public string DocTypeFieldName { get; set; }

        [Description("Thứ tự trường dữ liệu")]
        [BsonElement("doc_type_field_sort_order")]
        public int DocTypeFieldSortOrder { get; set; }    // Dư thừa dữ liệu

        [Description("InstanceId danh mục riêng")]
        [BsonElement("private_category_instance_id")]
        public Guid? PrivateCategoryInstanceId { get; set; } // Dư thừa dữ liệu

        [Description("InstanceId trường giá trị dữ liệu")]
        [BsonElement("doc_field_value_instance_id")]
        public Guid? DocFieldValueInstanceId { get; set; }   // Dư thừa dữ liệu, Job được link 1-1 đến DocFieldValue nào

        [Description("Kiểu nhập")]
        [BsonElement("input_type")]
        public short InputType { get; set; } = (short)EnumDocTypeField.InputType.InpText;  // refer EnumDocTypeField.InputType, Dư thừa dữ liệu để validate client

        [Description("Cho phép chọn nhiều")]
        [BsonElement("is_multiple_selection")]
        public bool? IsMultipleSelection { get; set; }      // Cho phép chọn nhiều, trong trường hợp InputType là PrivateCategory, Dư thừa dữ liệu

        [Description("Định dạng")]
        [BsonElement("format")]
        public string Format { get; set; }  // for date field, number field, ex: dd/MM/yyyy, Dư thừa dữ liệu để validate client

        [Description("Độ dài tối thiểu")]
        [BsonElement("min_length")]
        public int MinLength { get; set; }  // Dư thừa dữ liệu để validate client

        [Description("Độ dài tối đa")]
        [BsonElement("max_length")]
        public int MaxLength { get; set; }  // Dư thừa dữ liệu để validate client

        [Description("Giá trị tối thiểu")]
        [BsonElement("max_value")]
        public decimal? MaxValue { get; set; }  // Dư thừa dữ liệu để validate client

        [Description("Giá trị tối đa")]
        [BsonElement("min_value")]
        public decimal? MinValue { get; set; }  // Dư thừa dữ liệu để validate client

        [Description("Thứ tự sắp xếp ngẫu nhiên")]
        [BsonElement("random_sort_order")]
        public int RandomSortOrder { get; set; }    // Để lấy ngẫu nhiên công việc (trộn công việc)

        [BsonElement("project_type_instance_id")]
        public Guid? ProjectTypeInstanceId { get; set; }   // Dư thừa dữ liệu

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

        [Description("Giá trị")]
        [BsonElement("value")]
        public string Value { get; set; }

        [Description("File instanceId")]
        [BsonElement("file_instance_id")]
        public Guid? FileInstanceId { get; set; }  // Dư thừa dữ liệu, lấy từ DocFieldValue tương ứng

        [Description("File part instanceId")]
        [BsonElement("file_part_instance_id")]
        public Guid? FilePartInstanceId { get; set; }   // Dư thừa dữ liệu, lấy từ DocFieldValue tương ứng

        [Description("Vùng toạ độ")]
        [MaxLength(1024)]
        [BsonElement("coordinate_area")]
        public string CoordinateArea { get; set; }  // Dư thừa dữ liệu, lấy từ DocTypeField tương ứng

        [BsonElement("received_date")]
        public DateTime? ReceivedDate { get; set; }     // Thời gian nhận dữ liệu

        [BsonElement("due_date")]
        public DateTime? DueDate { get; set; }     // Thời gian đến hạn hoàn thành

        [BsonElement("created_date")]
        public DateTime? CreatedDate { get; set; } = DateTime.UtcNow;

        [BsonElement("created_by")]
        public Guid? CreatedBy { get; set; }

        [BsonElement("last_modification_date")]
        public DateTime? LastModificationDate { get; set; }

        [BsonElement("last_modified_by")]
        public Guid? LastModifiedBy { get; set; }

        [BsonElement("start_waiting_date")]
        public DateTime? StartWaitingDate { get; set; }     // Thời gian bắt đầu chuyển từ trạng thái Pending => Waiting

        [Description("Ghi chú")]
        //[MaxLength(128)]
        [BsonElement("note")]
        public string Note { get; set; }

        [Description("Bỏ qua")]
        [BsonElement("is_ignore")]
        public bool IsIgnore { get; set; } = false;         // Chỉ dùng trong màn DataEntry

        [Description("Lý do bỏ qua")]
        [MaxLength(128)]
        [BsonElement("reason_ignore")]
        public string ReasonIgnore { get; set; }            // Chỉ dùng trong màn DataEntry

        [Description("Cảnh báo")]
        [BsonElement("is_warning")]
        public bool IsWarning { get; set; } = false;

        [Description("Lý do cảnh báo")]
        [MaxLength(128)]
        [BsonElement("reason_warning")]
        public string ReasonWarning { get; set; }

        [Description("Có cập nhật value")]
        [BsonElement("has_change")]
        public bool HasChange { get; set; } = false;        // Có thay đổi giá trị so với bước TRƯỚC hay ko

        [Description("Old value lưu giá trị trước thay đổi")]
        [BsonElement("old_value")]
        public string OldValue { get; set; }                // Lưu trữ giá trị của bước TRƯỚC trong trường hợp HasChange = true, nếu HasChange = false thì OldValue = null

        [BsonElement("input")]
        public string Input { get; set; }

        [BsonElement("output")]
        public string Output { get; set; }

        [BsonElement("price")]
        public decimal Price { get; set; }

        [BsonElement("client_toll_ratio")]
        public decimal ClientTollRatio { get; set; }

        [BsonElement("work_toll_ratio")]
        public decimal WorkerTollRatio { get; set; }

        [Description("Thứ tự job khi ở chế độ share")]
        [BsonElement("share_job_sort_order")]
        public short ShareJobSortOrder { get; set; }

        [Description("Là công việc trong bước thuộc luồng song song?")]
        [BsonElement("is_parallel_job")]
        public bool IsParallelJob { get; set; } = false;

        [Description("Parallel instanceId")]
        [BsonElement("parallel_job_instance_id")]
        public Guid? ParallelJobInstanceId { get; set; }   // Gom nhóm các jobs cùng thuộc các bước song song

        [Description("Là công việc trong bước hội tụ?")]
        [BsonElement("is_convergence_job")]
        public bool IsConvergenceJob { get; set; } = false;

        [Description("Số lần retry trong bước tự động")]
        [BsonElement("retry_count")]
        public short RetryCount { get; set; }

        [Description("Vòng thực hiện thứ bao nhiêu")]
        [BsonElement("num_of_round")]
        public short NumOfRound { get; set; }
        
        [Description("Batch name")]
        [MaxLength(128)]
        [BsonElement("batch_name")]
        public string BatchName { get; set; }   // Gom nhóm các jobs cùng thuộc 1 lô (QA check)

        [Description("Batch instanceId")]
        [BsonElement("batch_job_instance_id")]
        public Guid? BatchJobInstanceId { get; set; }   // Gom nhóm các jobs cùng thuộc 1 lô (QA check)

        [Description("Trạng thái QA")]
        [BsonElement("qa_status")]
        public bool QaStatus { get; set; } = false;

        [BsonElement("tenant_id")]
        public int TenantId { get; set; }

        [BsonElement("right_status")]
        public short RightStatus { get; set; } = (short)EnumJob.RightStatus.WaitingConfirm;   // refer: EnumJob.RightStatus: Chờ confirm, Đúng, Sai

        [BsonElement("status")]
        public short Status { get; set; } = (short)EnumJob.Status.Waiting;   // refer: EnumJob.Status: Chờ phân phối, Đang xử lý, Hoàn thành

        [NotMapped]
        public string InputShortNote { get; set; }
        #endregion

        public object Clone()
        {
            return MemberwiseClone();
        }
    }
}
