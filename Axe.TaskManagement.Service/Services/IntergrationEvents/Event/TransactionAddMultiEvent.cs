using Ce.EventBus.Lib.Events;
using System.Collections.Generic;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Event
{
    public class TransactionAddMultiEvent : IntegrationEvent
    {
        public string CorrelationMessage { get; set; }

        public string CorrelationDescription { get; set; }

        public List<ItemTransactionAdd> ItemTransactionAdds { get; set; }

        public List<ItemTransactionToSysWalletAdd> ItemTransactionToSysWalletAdds { get; set; }

        public string AccessToken { get; set; }
    }
}
