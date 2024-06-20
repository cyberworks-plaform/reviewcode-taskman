using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using Ce.Constant.Lib.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IQueueLockService : IMongoBaseService<QueueLock, QueueLockDto>
    {
        Task<GenericResponse<List<QueueLockDto>>> UpSertMultiQueueLockAsync(List<QueueLockDto> models);

        Task<GenericResponse<long>> DeleteQueueLockCompleted(Guid docInstanceId, Guid workflowStepInstanceId,
            Guid? docTypeFieldInstanceId = null, Guid? docFieldValueInstanceId = null);
    }
}