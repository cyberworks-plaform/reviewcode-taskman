using System;
using System.ComponentModel.DataAnnotations;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class ItemTransactionToSysWalletAdd
    {
        [Required]
        public Guid SourceUserInstanceId { get; set; }

        public decimal ChangeAmount { get; set; }

        public string JobCode { get; set; }

        public string Message { get; set; }

        public string Description { get; set; }
    }
}
