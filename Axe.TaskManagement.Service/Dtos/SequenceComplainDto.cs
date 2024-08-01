using Ce.Common.Lib.Interfaces;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Axe.TaskManagement.Service.Dtos
{
    public class SequenceComplainDto : IEntity<string>
    {
        [Key]
        [Required]
        [BsonId]
        [BsonElement("_id")]
        public string Id { get; set; }

        [BsonElement("sequence_name")]
        public string SequenceName { get; set; }

        [BsonElement("sequence_value")]
        public long SequenceValue { get; set; }
    }
}
