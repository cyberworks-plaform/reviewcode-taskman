using AutoMapper;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Common.Lib.Services;
using Ce.Constant.Lib.Dtos;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class QueueLockService : MongoBaseService<QueueLock, QueueLockDto>, IQueueLockService
    {
        private readonly IQueueLockRepository _repository;
        public QueueLockService(IQueueLockRepository repos, IMapper mapper, IUserPrincipalService userPrincipalService) : base(repos, mapper, userPrincipalService)
        {
            _repository = repos;
        }

        public async Task<GenericResponse<List<QueueLockDto>>> UpSertMultiQueueLockAsync(List<QueueLockDto> models)
        {
            GenericResponse<List<QueueLockDto>> response;
            try
            {
                var entities = _mapper.Map<List<QueueLockDto>, List<QueueLock>>(models);
                var data = await _repository.UpSertMultiQueueLockAsync(entities);
                var result = _mapper.Map<List<QueueLock>, List<QueueLockDto>>(data);
                response = GenericResponse<List<QueueLockDto>>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<QueueLockDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<long>> DeleteQueueLockCompleted(Guid docInstanceId, Guid workflowStepInstanceId, Guid? docTypeFieldInstanceId = null,
            Guid? docFieldValueInstanceId = null)
        {
            GenericResponse<long> response;
            try
            {
                var result = await _repository.DeleteQueueLockCompleted(docInstanceId, workflowStepInstanceId,
                    docTypeFieldInstanceId, docFieldValueInstanceId);
                response = GenericResponse<long>.ResultWithData(result);
            }
            catch (Exception ex)
            {
                response = GenericResponse<long>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
    }
}
