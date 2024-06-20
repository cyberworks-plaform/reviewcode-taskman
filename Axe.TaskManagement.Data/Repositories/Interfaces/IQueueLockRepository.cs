using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Axe.TaskManagement.Model.Entities;
using Ce.Common.Lib.MongoDbBase.Interfaces;

namespace Axe.TaskManagement.Data.Repositories.Interfaces
{
    public interface IQueueLockRepository : IMongoBaseRepository<QueueLock>
    {
        Task<List<QueueLock>> UpSertMultiQueueLockAsync(IEnumerable<QueueLock> entities);

        Task<long> DeleteQueueLockCompleted(Guid docInstanceId, Guid workflowStepInstanceId,
            Guid? docTypeFieldInstanceId = null, Guid? docFieldValueInstanceId = null);
    }
}