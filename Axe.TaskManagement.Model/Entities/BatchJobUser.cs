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
    [Table("BatchJobUser")]
    [Description("Save info who can process what batch")]
    public class BatchJobUser : IEntity<ObjectId>
    {
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Description("Khóa chính")]
        [BsonId]
        public ObjectId Id { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [BsonElement("instance_id")]
        public Guid InstanceId { get; set; }

        [BsonElement("project_instance_id")]
        public Guid? ProjectInstanceId { get; set; }

        [BsonElement("path_instance_id")]
        public Guid? PathInstanceId { get; set; }

        [BsonElement("batch_instance_id")]
        public Guid? BatchInstanId { get; set; }

        [Description("Batch name")]
        [MaxLength(128)]
        [BsonElement("batch_name")]
        public string BatchName { get; set; }   

        [BsonElement("user_instance_id")]
        public Guid? UserInstanceId { get; set; }     

        [BsonElement("created_date")]
        public DateTime Status { get; set; } = DateTime.Now;
    }
}
