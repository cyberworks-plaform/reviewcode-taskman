using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.Utility.Dtos;
using Axe.Utility.Enums;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using Ce.Constant.Lib.Dtos;
using System;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface ITaskService : IMongoBaseService<TaskEntity, TaskDto>
    {
        Task<GenericResponse<bool>> DeleteByDocInstanceIdAsync(Guid docInstanceId);

        Task<GenericResponse<bool>> ChangeStatus(string id, short newStatus = (short)EnumTask.Status.Processing);

        Task<GenericResponse<TaskDto>> UpdateProgressValue(UpdateTaskStepProgressDto updateTaskStepProgress);

        Task<GenericResponse<TaskDto>> GetByDocInstanceId(Guid docInstanceId);
    }
}