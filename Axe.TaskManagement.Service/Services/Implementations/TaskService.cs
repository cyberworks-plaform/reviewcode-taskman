using AutoMapper;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.Utility.Dtos;
using Axe.Utility.Enums;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Common.Lib.Services;
using Ce.Constant.Lib.Dtos;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class TaskService : MongoBaseService<TaskEntity, TaskDto>, ITaskService
    {
        private readonly ITaskRepository _repository;

        public TaskService(ITaskRepository repos, IMapper mapper, IUserPrincipalService userPrincipalService) : base(repos, mapper, userPrincipalService)
        {
            _repository = repos;
        }

        public async Task<GenericResponse<bool>> DeleteByDocInstanceIdAsync(Guid docInstanceId)
        {
            GenericResponse<bool> response;
            try
            {
                response = GenericResponse<bool>.ResultWithData(await _repository.DeleteByDocInstanceIdAsync(docInstanceId));
            }
            catch (Exception ex)
            {
                response = GenericResponse<bool>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<bool>> ChangeStatus(string id, short newStatus = (short)EnumTask.Status.Processing)
        {
            GenericResponse<bool> response;
            try
            {
                response = GenericResponse<bool>.ResultWithData(await _repository.ChangeStatus(id, newStatus));
            }
            catch (Exception ex)
            {
                response = GenericResponse<bool>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<TaskDto>> UpdateProgressValue(UpdateTaskStepProgressDto updateTaskStepProgress)
        {
            GenericResponse<TaskDto> response;
            try
            {
                var data = await _repository.UpdateProgressValue(updateTaskStepProgress.Id, updateTaskStepProgress.ChangeTaskStepProgress, updateTaskStepProgress.NewStatus);
                var result = _mapper.Map<TaskDto>(data);
                response = GenericResponse<TaskDto>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<TaskDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }


        public async Task<GenericResponse<TaskDto>> GetByDocInstanceId(Guid docInstanceId)
        {
            GenericResponse<TaskDto> response;
            try
            {
                var data = await _repository.GetByDocInstanceId(docInstanceId);
                var result = _mapper.Map<TaskDto>(data);
                response = GenericResponse<TaskDto>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<TaskDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
    }
}
