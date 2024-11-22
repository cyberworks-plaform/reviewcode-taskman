using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using System.Net;
using System.ComponentModel.DataAnnotations.Schema;

namespace Axe.TaskManagement.Service.Dtos
{
    [Table("ExtendedInboxIntegrationEvents")]
    [Description("Extended Inbox integration event")]
    public class ExtendedInboxIntegrationEventDto
    {
        public Guid IntergrationEventId { get; set; }

        public Guid EventBusIntergrationEventId { get; set; }

        public DateTime EventBusIntergrationEventCreationDate { get; set; }

        public string EntityName { get; set; }

        public string EntityId { get; set; }

        public Guid? EntityInstanceId { get; set; }

        public string EntityIds { get; set; }

        public string EntityInstanceIds { get; set; }

        [MaxLength(128)]
        public string ExchangeName { get; set; }

        [MaxLength(64)]
        public string VirtualHost { get; set; } = "/";

        [MaxLength(64)]
        public string ServiceCode { get; set; }

        [MaxLength(128)]
        public string ServiceInstanceId { get; set; } = Dns.GetHostName();

        [MaxLength(128)]
        public string ServiceInstanceIdProcessed { get; set; } = Dns.GetHostName();

        public string Data { get; set; }

        public short Priority { get; set; }

        public short RetryCount { get; set; }

        public string Message { get; set; }

        public string StackTrace { get; set; }

        public DateTime? LastModificationDate { get; set; }

        public short Status { get; set; }

        [Description("Id Project")]
        public int ProjectId { get; set; }

        public Guid? ProjectInstanceId { get; set; }

        public string Path { get; set; }                    // Lưu path SyncMetaId theo định dạng Id1/Id2/Id3

        public Guid? DocInstanceId { get; set; }
    }
}
