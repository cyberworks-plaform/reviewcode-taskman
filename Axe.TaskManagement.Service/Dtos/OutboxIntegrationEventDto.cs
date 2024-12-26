using System;
using System.Net;

namespace Axe.TaskManagement.Service.Dtos
{
    public class OutboxIntegrationEventDto
    {
        public long Id { get; set; }

        public string EntityName { get; set; }

        public string EntityId { get; set; }

        public Guid? EntityInstanceId { get; set; }

        public string ExchangeName { get; set; }

        public string ServiceCode { get; set; }

        public string ServiceInstanceId { get; set; } = Dns.GetHostName();


        public string Data { get; set; }

        public DateTime? LastModificationDate { get; set; }

        public short Status { get; set; }
    }
}
