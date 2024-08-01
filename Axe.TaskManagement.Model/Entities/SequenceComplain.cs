using Ce.Common.Lib.Interfaces;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Axe.TaskManagement.Model.Entities
{
    public class SequenceComplain : IEntity<ObjectId>
    {
        [Key]
        [Required]
        [BsonId]
        [BsonElement("_id")]
        public ObjectId Id { get; set; }

        [BsonElement("sequence_name")]
        public string SequenceName { get; set; }

        [BsonElement("sequence_value")]
        public long SequenceValue { get; set; }
    }
}
