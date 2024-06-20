using System;

namespace Axe.TaskManagement.Service.Dtos
{
    public class DocLockStatusDto
    {
        public Guid InstanceId { get; set; }

        public bool IsLocked { get; set; }
    }
}
