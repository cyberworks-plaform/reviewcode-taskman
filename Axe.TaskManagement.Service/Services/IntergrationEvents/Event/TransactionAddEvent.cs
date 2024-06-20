using Ce.EventBus.Lib.Events;
using System;
using System.ComponentModel.DataAnnotations;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class TransactionAddEvent : IntegrationEvent
    {
        [Required]
        public Guid SourceUserInstanceId { get; set; }

        [Required]
        public Guid DestinationUserInstanceId { get; set; }

        public decimal ChangeAmount { get; set; }

        public decimal ChangeProvisionalAmount { get; set; }

        public string JobCode { get; set; }

        public string Message { get; set; }

        public string Description { get; set; }

        public string AccessToken { get; set; }
    }
}
