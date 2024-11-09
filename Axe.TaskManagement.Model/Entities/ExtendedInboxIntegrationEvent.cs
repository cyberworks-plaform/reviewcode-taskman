using Ce.Constant.Lib.Dtos;
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Axe.TaskManagement.Model.Entities
{
    [Table("ExtendedInboxIntegrationEvents")]
    [Description("Extended Inbox integration event")]
    public class ExtendedInboxIntegrationEvent : InboxIntegrationEvent
    {
        public Guid? ProjectInstanceId { get; set; }

        public string Path { get; set; }                    // Lưu path SyncMetaId theo định dạng Id1/Id2/Id3

        public Guid? DocInstanceId { get; set; }
    }
}
