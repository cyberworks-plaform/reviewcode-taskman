using Axe.TaskManagement.Model.Entities;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;

namespace Axe.TaskManagement.Data.Repositories.Interfaces
{
    public interface ITaskRepository : IMongoBaseRepository<TaskEntity>
    {
        Task<bool> ChangeStatus(string id, short newStatus = (short)EnumTask.Status.Processing);

        Task<bool> ChangeStatusExternal(Guid instanceId, short newStatus = (short)EnumTask.Status.Processing);

        Task<bool> ChangeStatusMulti(List<string> ids, short newStatus = (short)EnumTask.Status.Processing);

        Task<TaskEntity> UpdateProgressValue(string id, TaskStepProgress changeTaskStepProgress, short? newStatus = null);

        Task<bool> DeleteByDocInstanceIdAsync(Guid docInstanceId);

        Task<TaskEntity> GetByDocInstanceId(Guid docInstanceId);
    }
}